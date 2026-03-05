// ========== VIDEO CALL FUNCTIONALITY (MULTI-PEER) ==========
let peerConnections = {}; // { userId: RTCPeerConnection }
let remoteStreams = {}; // { userId: MediaStream }
let localStream = null;
let isCallActive = false;
let isCaller = false;
let callConversationId = null;
let callOtherUserId = null; // For 1-1, or 0 for group
let isGroupCall = false;
let cameraEnabled = true;
let microphoneEnabled = true;
let incomingCallData = null;
let pendingCandidates = {}; // { userId: [candidates] }
let rejectedCount = 0;
let callTimeout = null;
let userAvatars = {}; // { userId: avatarUrl }
let currentCallIsAudioOnly = false;

let expectedParticipants = 0;
let ringtoneAudio = new Audio('/sounds/ringtone.mp3');
let ringbackAudio = new Audio('/sounds/dialing.mp3');
ringtoneAudio.loop = true;
ringbackAudio.loop = true;
const configuration = {
    iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' },
        { urls: 'stun:stun2.l.google.com:19302' }
    ]
};
// Kiểm tra hỗ trợ getUserMedia
function checkMediaSupport() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        return false;
    }

    // Kiểm tra HTTPS hoặc localhost
    const isSecure = location.protocol === 'https:' ||
        location.hostname === 'localhost' ||
        location.hostname === '127.0.0.1';

    return isSecure;
}

// Khởi tạo cuộc gọi video
async function initiateVideoCall(conversationId, otherUserId, isGroup = false, audioOnly = false) {
    if (!conversationId) {
        alert("Vui lòng chọn một cuộc trò chuyện");
        return;
    }

    // Kiểm tra hỗ trợ media
    if (!checkMediaSupport()) {
        alert("Tính năng gọi video chỉ hoạt động trên HTTPS hoặc localhost. Vui lòng truy cập qua HTTPS để sử dụng tính năng này.");
        return;
    }

    try {
        isCaller = true;
        callConversationId = parseInt(conversationId);
        callOtherUserId = parseInt(otherUserId) || 0;
        callConversationId = parseInt(conversationId);
        callOtherUserId = parseInt(otherUserId) || 0;
        isGroupCall = isGroup;
        isCallActive = true;
        currentCallIsAudioOnly = audioOnly;

        if (isNaN(callConversationId)) {
            alert("Lỗi: ID cuộc trò chuyện không hợp lệ");
            return;
        }

        // Lấy stream từ camera và microphone
        localStream = await navigator.mediaDevices.getUserMedia({
            video: true,
            audio: true
        });

        // Handle Audio Only Mode
        if (audioOnly) {
            cameraEnabled = false;
            localStream.getVideoTracks().forEach(track => track.enabled = false);
            document.getElementById("localVideoPlaceholder").classList.remove("hidden");
            updateCameraButton();
        } else {
            document.getElementById("localVideoPlaceholder").classList.add("hidden");
        }

        document.getElementById("localVideo").srcObject = localStream;

        // Gửi thông báo bắt đầu cuộc gọi
        expectedParticipants = await chatConnection.invoke("InitiateVideoCall",
            callConversationId,
            callOtherUserId,
            audioOnly
        );
        rejectedCount = 0;
        // Hiển thị modal
        document.getElementById("videoCallModal").classList.remove("hidden");
        document.getElementById("callStatus").textContent = audioOnly ? "Đang gọi thoại..." : "Đang gọi...";
        document.getElementById("callStatusText").textContent = "Đang chờ người dùng phản hồi...";

        // Play ringback tone
        playRingback();

        // 60s timeout
        callTimeout = setTimeout(() => {
            if (isCallActive && isCaller) {
                // Tự động kết thúc nếu không ai bắt máy
                endVideoCall();
            }
        }, 60000);

        // Reset container
        document.getElementById("remoteVideosContainer").innerHTML = "";
    } catch (error) {
        console.error("Error initiating video call:", error);
        alert("Không thể khởi tạo cuộc gọi video: " + error.message);
        cleanupCall();
    }
}
// Chấp nhận cuộc gọi đến
async function acceptIncomingCall() {
    if (!incomingCallData) return;

    // Kiểm tra hỗ trợ media
    if (!checkMediaSupport()) {
        alert("Tính năng gọi video chỉ hoạt động trên HTTPS hoặc localhost. Vui lòng truy cập qua HTTPS để sử dụng tính năng này.");
        rejectIncomingCall();
        return;
    }

    try {
        isCaller = false;
        callConversationId = incomingCallData.conversationId;
        callOtherUserId = incomingCallData.callerId; // The caller
        isGroupCall = false; // Reset, will be handled by logic if needed. 
        currentCallIsAudioOnly = incomingCallData.audioOnly;

        // Store caller avatar
        userAvatars[incomingCallData.callerId] = incomingCallData.callerAvatar;

        // Lấy stream từ camera và microphone
        localStream = await navigator.mediaDevices.getUserMedia({
            video: true,
            audio: true
        });

        // Handle Audio Only Mode (Sync with Caller)
        if (incomingCallData.audioOnly) {
            cameraEnabled = false;
            localStream.getVideoTracks().forEach(track => track.enabled = false);
            document.getElementById("localVideoPlaceholder").classList.remove("hidden");
            updateCameraButton();
        } else {
            document.getElementById("localVideoPlaceholder").classList.add("hidden");
        }

        document.getElementById("localVideo").srcObject = localStream;
        document.getElementById("remoteVideosContainer").innerHTML = "";
        // Đóng modal incoming call
        document.getElementById("incomingCallModal").classList.add("hidden");
        // Mở modal video call
        document.getElementById("videoCallModal").classList.remove("hidden");
        document.getElementById("callStatus").textContent = "Đang kết nối...";
        document.getElementById("callStatusText").textContent = "Đang thiết lập kết nối...";
        // Gửi thông báo chấp nhận
        await chatConnection.invoke("AcceptVideoCall",
            callConversationId,
            callOtherUserId
        );
        stopSounds();
    } catch (error) {
        console.error("Error accepting call:", error);
        alert("Không thể chấp nhận cuộc gọi: " + error.message);
        cleanupCall();
    }
}
function createPeerConnection(targetUserId) {
    if (peerConnections[targetUserId]) return peerConnections[targetUserId];
    const pc = new RTCPeerConnection(configuration);
    peerConnections[targetUserId] = pc;
    pc.onicecandidate = (event) => {
        if (event.candidate) {
            chatConnection.invoke("SendIceCandidate",
                parseInt(callConversationId),
                targetUserId,
                JSON.stringify(event.candidate),
                event.candidate.sdpMid,
                event.candidate.sdpMLineIndex
            ).catch(err => console.error("Error sending ICE candidate:", err));
        }
    };
    pc.oniceconnectionstatechange = () => {
        const state = pc.iceConnectionState;
        console.log(`ICE State for ${targetUserId}: ${state}`);

        // Update UI based on state (simplified for group)
        const statusText = document.getElementById("callStatusText");
        const statusHeader = document.getElementById("callStatus");

        if (state === "connected" || state === "completed") {
            if (statusText) {
                statusText.textContent = "Đã kết nối";
                statusText.classList.remove("text-red-500");
                statusText.classList.add("text-green-500");
            }
            if (statusHeader) {
                // Detect mode from initial call? We need to store it or guess.
                // Ideally store isAudioOnly in global scope.
                // For now, let's just say "Cuộc gọi đang diễn ra" or "Đã kết nối".
                // Or revert to checking incomingCallData.audioOnly or initiateVideoCall flag?
                // But this function createPeerConnection is called later.
                // Let's use a global var `currentCallIsAudioOnly`.
                if (typeof currentCallIsAudioOnly !== 'undefined' && currentCallIsAudioOnly) {
                    statusHeader.textContent = "Đang gọi thoại";
                } else {
                    statusHeader.textContent = "Đang gọi video";
                }
            }
            document.getElementById("callStatusOverlay").classList.add("hidden");
        } else if (state === "failed" || state === "disconnected") {
            // Handle disconnection of a specific peer
            removeRemoteVideo(targetUserId);
            delete peerConnections[targetUserId];
            if (Object.keys(peerConnections).length === 0) {
                // If everyone left?
                // statusText.textContent = "Kết nối bị gián đoạn";
            }
        }
    };
    pc.ontrack = (event) => {
        console.log(`Received track from ${targetUserId}`);
        if (event.streams && event.streams[0]) {
            addRemoteVideo(targetUserId, event.streams[0]);
        }
    };
    // Add local tracks
    if (localStream) {
        localStream.getTracks().forEach(track => {
            pc.addTrack(track, localStream);
        });
    }
    return pc;
}
function addRemoteVideo(userId, stream) {
    if (remoteStreams[userId]) return; // Already added
    remoteStreams[userId] = stream;
    const container = document.getElementById("remoteVideosContainer");
    const wrapper = document.createElement("div");
    wrapper.id = `remote-wrapper-${userId}`;
    wrapper.className = "relative w-full h-full bg-black overflow-hidden rounded-lg";

    const video = document.createElement("video");
    video.id = `remote-video-${userId}`;
    video.autoplay = true;
    video.playsInline = true;
    video.className = "w-full h-full object-cover relative z-10";
    video.srcObject = stream;
    video.play().catch(e => console.log("Remote video play error", e));

    // AVATAR OVERLAY
    const avatarUrl = userAvatars[userId] || '/Uploads/default-avatar.png';
    // Logic: Nếu audioOnly=true, hoặc track disabled, hiện avatar.
    // Use global variable currentCallIsAudioOnly to handle both Caller and Receiver cases
    let showAvatar = false;
    if (typeof currentCallIsAudioOnly !== 'undefined' && currentCallIsAudioOnly) {
        showAvatar = true;
    } else if (incomingCallData && incomingCallData.audioOnly) {
        showAvatar = true;
    }

    const overlay = document.createElement("div");
    overlay.id = `remote-overlay-${userId}`;
    overlay.className = `absolute inset-0 flex items-center justify-center bg-gray-800 text-white z-20 ${showAvatar ? '' : 'hidden'}`;
    overlay.innerHTML = `
        <div class="text-center w-full h-full relative">
             <img src="${avatarUrl}" class="w-full h-full object-cover opacity-50" />
            <div class="absolute inset-0 flex flex-col items-center justify-center">
                 <div class="text-2xl md:text-4xl mb-2 drop-shadow-md"><i class="fa-solid fa-microphone-lines"></i></div>
            </div>
        </div>
    `;

    wrapper.appendChild(video);
    wrapper.appendChild(overlay);
    container.appendChild(wrapper);
    updateGrid();

    // Listen for track mute/unmute (Camera toggles)
    const videoTrack = stream.getVideoTracks()[0];
    if (videoTrack) {

        videoTrack.onmute = () => {
            console.log(`Remote video track muted (User ${userId})`);
            overlay.classList.remove("hidden");
        };

        videoTrack.onunmute = () => {
            console.log(`Remote video track unmuted (User ${userId})`);
            overlay.classList.add("hidden");
        };
    }
}
function removeRemoteVideo(userId) {
    const wrapper = document.getElementById(`remote-wrapper-${userId}`);
    if (wrapper) wrapper.remove();
    delete remoteStreams[userId];
    updateGrid();
}
function updateGrid() {
    const container = document.getElementById("remoteVideosContainer");
    const count = container.children.length;

    // Dynamic grid logic
    let gridClass = "w-full h-full grid gap-2 p-2 bg-black overflow-hidden ";

    if (count <= 1) {
        gridClass += "grid-cols-1 grid-rows-1";
    } else if (count === 2) {
        // 2 remote users: Side by side on desktop, stacked on mobile
        gridClass += "grid-cols-1 md:grid-cols-2 grid-rows-2 md:grid-rows-1";
    } else if (count <= 4) {
        // 3-4 users: 2x2 grid
        gridClass += "grid-cols-2 grid-rows-2";
    } else if (count <= 6) {
        // 5-6 users: 3x2 grid
        gridClass += "grid-cols-2 md:grid-cols-3 grid-rows-3 md:grid-rows-2";
    } else {
        // 7+ users: 3x3 grid (max 9 visible usually)
        gridClass += "grid-cols-3 grid-rows-3";
    }

    container.className = gridClass;
}
// Từ chối cuộc gọi đến
async function rejectIncomingCall() {
    if (!incomingCallData) return;
    await chatConnection.invoke("RejectVideoCall",
        incomingCallData.conversationId,
        incomingCallData.callerId
    );
    document.getElementById("incomingCallModal").classList.add("hidden");
    incomingCallData = null;
    isCallActive = false;
    stopSounds();
}
// Kết thúc cuộc gọi
async function endVideoCall() {
    const targetId = isCaller ? 0 : (callOtherUserId || 0);

    if (callConversationId) {
        await chatConnection.invoke("EndVideoCall",
            parseInt(callConversationId),
            targetId
        );
        stopSounds();
    }
    cleanupCall();
}
// Dọn dẹp sau cuộc gọi
function cleanupCall() {
    stopSounds();
    if (localStream) {
        localStream.getTracks().forEach(track => track.stop());
        localStream = null;
    }
    // Close all peer connections
    Object.values(peerConnections).forEach(pc => pc.close());
    peerConnections = {};
    remoteStreams = {};
    pendingCandidates = {};
    const localVideo = document.getElementById("localVideo");
    if (localVideo) localVideo.srcObject = null;

    document.getElementById("remoteVideosContainer").innerHTML = "";
    document.getElementById("localVideoPlaceholder")?.classList.remove("hidden");
    document.getElementById("callStatusOverlay")?.classList.remove("hidden");
    document.getElementById("videoCallModal")?.classList.add("hidden");
    document.getElementById("incomingCallModal")?.classList.add("hidden");
    isCallActive = false;
    isCaller = false;
    callConversationId = null;
    callOtherUserId = null;
    isGroupCall = false;
    cameraEnabled = true;
    microphoneEnabled = true;
    microphoneEnabled = true;
    incomingCallData = null;
    rejectedCount = 0;
    expectedParticipants = 0;

    if (callTimeout) {
        clearTimeout(callTimeout);
        callTimeout = null;
    }

    updateCameraButton();
    updateMicrophoneButton();
}
// Bật/tắt camera
function toggleCamera() {
    if (!localStream) return;
    cameraEnabled = !cameraEnabled;

    // Toggle track
    localStream.getVideoTracks().forEach(track => {
        track.enabled = cameraEnabled;
    });

    // Update UI Local
    if (!cameraEnabled) {
        document.getElementById("localVideoPlaceholder").classList.remove("hidden");
    } else {
        document.getElementById("localVideoPlaceholder").classList.add("hidden");
    }
    updateCameraButton();

    // Notify Server
    if (callConversationId) {
        chatConnection.invoke("ToggleCamera", parseInt(callConversationId), cameraEnabled)
            .catch(err => console.error("Error toggling camera signal:", err));
    }
}
// Bật/tắt microphone
function toggleMicrophone() {
    if (!localStream) return;
    microphoneEnabled = !microphoneEnabled;
    localStream.getAudioTracks().forEach(track => {
        track.enabled = microphoneEnabled;
    });
    updateMicrophoneButton();
}
function updateCameraButton() {
    const icon = document.getElementById("cameraIcon");
    const text = document.getElementById("cameraText");
    if (cameraEnabled) {
        icon.innerHTML = '<i class="fa-solid fa-video"></i>';
        text.textContent = "Tắt camera";
    } else {
        icon.innerHTML = '<i class="fa-solid fa-camera"></i>';
        text.textContent = "Bật camera";
    }
}
function updateMicrophoneButton() {
    const icon = document.getElementById("micIcon");
    const text = document.getElementById("micText");
    if (microphoneEnabled) {
        icon.innerHTML = '<i class="fa-solid fa-microphone"></i>';
        text.textContent = "Tắt mic";
    } else {
        icon.innerHTML = '<i class="fa-solid fa-microphone-slash"></i>';
        text.textContent = "Bật mic";
    }
}

function playRingback() {
    ringbackAudio.currentTime = 0;
    ringbackAudio.play().catch(e => console.log("Audio play failed (interaction needed?):", e));
}

function playRingtone() {
    ringtoneAudio.currentTime = 0;
    ringtoneAudio.play().catch(e => console.log("Audio play failed (interaction needed?):", e));
}

function stopSounds() {
    ringbackAudio.pause();
    ringbackAudio.currentTime = 0;
    ringtoneAudio.pause();
    ringtoneAudio.currentTime = 0;
}

// ========== SIGNALR EVENT LISTENERS ==========

if (typeof chatConnection !== 'undefined') {
    // 1. Nhận cuộc gọi đến
    chatConnection.on("IncomingVideoCall", (data) => {
        console.log("Incoming call:", data);
        if (isCallActive) {
            // Đang bận -> Tự động từ chối
            chatConnection.invoke("RejectVideoCall", data.conversationId, data.callerId);
            return;
        }

        incomingCallData = data;
        isCallActive = true;

        document.getElementById("incomingCallerName").textContent = data.callerName;
        document.getElementById("incomingCallModal").classList.remove("hidden");

        playRingtone();
    });

    // 2. Người kia chấp nhận cuộc gọi
    chatConnection.on("VideoCallAccepted", async (data) => {
        console.log("Call accepted:", data);
        document.getElementById("callStatus").textContent = "Đang kết nối...";
        document.getElementById("callStatusText").textContent = "Người dùng đã chấp nhận. Đang kết nối...";
        stopSounds();

        // Store Answerer Avatar
        if (data.answererId && data.answererAvatar) {
            userAvatars[data.answererId] = data.answererAvatar;
        }

        if (callTimeout) {
            clearTimeout(callTimeout);
            callTimeout = null;
        }

        const pc = createPeerConnection(data.answererId);

        try {
            const offer = await pc.createOffer();
            await pc.setLocalDescription(offer);

            await chatConnection.invoke("SendOffer",
                data.conversationId,
                data.answererId,
                JSON.stringify(offer)
            );
        } catch (err) {
            console.error("Error creating offer:", err);
        }
    });

    // 3. Người kia từ chối cuộc gọi
    chatConnection.on("VideoCallRejected", (data) => {
        stopSounds();
        if (!isCaller) return;

        if (isGroupCall) {
            rejectedCount++;
            if (expectedParticipants > 0 && rejectedCount >= expectedParticipants) {
                document.getElementById("callStatusText").textContent = "Tất cả đã từ chối";
                cleanupCall();
                const toast = document.createElement("div");
                toast.className = "fixed inset-0 flex items-center justify-center z-[9999] pointer-events-none";
                toast.innerHTML = `<div class="bg-red-600 text-white px-8 py-6 rounded-2xl shadow-2xl text-xl font-bold animate-pulse">Không ai tham gia</div>`;
                document.body.appendChild(toast);
                setTimeout(() => toast.remove(), 3000);
            } else {
                const toast = document.createElement("div");
                toast.className = "fixed top-20 right-4 bg-yellow-600 text-white px-6 py-3 rounded-lg shadow-lg z-50";
                toast.textContent = `${data.rejectorName || "Một người"} đã từ chối`;
                document.body.appendChild(toast);
                setTimeout(() => toast.remove(), 3000);
            }
        } else {
            document.getElementById("callRejectedModal").classList.remove("hidden");
            setTimeout(() => document.getElementById("callRejectedModal").classList.add("hidden"), 4000);
            cleanupCall();
        }
    });

    // 4. Cuộc gọi kết thúc
    chatConnection.on("VideoCallEnded", (data) => {
        console.log("Call ended:", data);
        if (isCallActive || document.getElementById("videoCallModal").classList.contains("hidden") === false) {
            document.getElementById("callEndedModal").classList.remove("hidden");
            setTimeout(() => {
                closeCallEndedModal();
            }, 4000);
        }
        cleanupCall();
    });

    // 5. Nhận Offer
    chatConnection.on("ReceiveOffer", async (data) => {
        console.log("Received Offer:", data);
        const pc = createPeerConnection(data.fromUserId);

        try {
            const offerDesc = new RTCSessionDescription(JSON.parse(data.offer));
            await pc.setRemoteDescription(offerDesc);
            const answer = await pc.createAnswer();
            await pc.setLocalDescription(answer);

            await chatConnection.invoke("SendAnswer",
                data.conversationId,
                data.fromUserId,
                JSON.stringify(answer)
            );

            if (pendingCandidates[data.fromUserId]) {
                for (const candidate of pendingCandidates[data.fromUserId]) {
                    await pc.addIceCandidate(candidate);
                }
                delete pendingCandidates[data.fromUserId];
            }
        } catch (err) {
            console.error("Error handling offer:", err);
        }
    });

    // 6. Nhận Answer
    chatConnection.on("ReceiveAnswer", async (data) => {
        console.log("Received Answer:", data);
        const pc = peerConnections[data.fromUserId];
        if (pc) {
            try {
                const answerDesc = new RTCSessionDescription(JSON.parse(data.answer));
                await pc.setRemoteDescription(answerDesc);
            } catch (err) {
                console.error("Error setting remote description (answer):", err);
            }
        }
    });

    // 7. Nhận ICE Candidate
    chatConnection.on("ReceiveIceCandidate", async (data) => {
        const candidate = new RTCIceCandidate(JSON.parse(data.candidate));
        const pc = peerConnections[data.fromUserId];

        if (pc) {
            if (pc.remoteDescription) {
                try {
                    await pc.addIceCandidate(candidate);
                } catch (err) {
                    console.error("Error adding ICE candidate:", err);
                }
            } else {
                if (!pendingCandidates[data.fromUserId]) {
                    pendingCandidates[data.fromUserId] = [];
                }
                pendingCandidates[data.fromUserId].push(candidate);
            }
        }
    });

    // 8. User Left Call
    chatConnection.on("UserLeftCall", (data) => {
        console.log("User left call:", data);
        removeRemoteVideo(data.userId);
    });

    // 9. Camera Toggled (Remote User)
    chatConnection.on("ReceiverToggleCamera", (data) => {
        // data = { userId, isEnabled }
        const overlay = document.getElementById(`remote-overlay-${data.userId}`);
        if (overlay) {
            if (data.isEnabled) {
                overlay.classList.add("hidden");
            } else {
                overlay.classList.remove("hidden");
            }

            // Also simplify mute logic override?
            // The mute/unmute track event is also reliable but sometimes slow or not fired on some browsers.
            // This explicit signal ensures UI update.
        }
    });

} else {
    console.error("chatConnection is not defined. Video call listeners not attached.");
}

// Function để gọi từ chat.js
window.checkVideoCallSupport = function checkVideoCallSupport() {
    // console.log("Checking video call support...");
    // updateVideoCallButton removed to avoid conflict. Logic moved to Index.cshtml
    if (typeof window.updateVideoCallButton === 'function') {
        window.updateVideoCallButton();
    }
};
