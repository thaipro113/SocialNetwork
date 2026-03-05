// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ========================================
// TIME AGO & UPDATE TIMES SYSTEM (MOVED FROM Profile.cshtml)
// ========================================
function timeAgo(date) {
    const now = new Date();
    const past = new Date(date);
    const diff = Math.floor((now - past) / 1000); // giây

    if (diff < 60) return "Vừa xong";
    if (diff < 3600) return `${Math.floor(diff / 60)} phút trước`;
    if (diff < 86400) return `${Math.floor(diff / 3600)} giờ trước`;
    if (diff < 2592000) return `${Math.floor(diff / 86400)} ngày trước`;
    if (diff < 31104000) return `${Math.floor(diff / 2592000)} tháng trước`;
    return `${Math.floor(diff / 31104000)} năm trước`;
}

function updateTimes() {
    document.querySelectorAll(".time-ago").forEach(el => {
        const time = el.dataset.time;
        if (time) {
            el.innerText = timeAgo(time);
        }
    });
}

// Auto-run updateTimes on load and every minute
document.addEventListener("DOMContentLoaded", function () {
    updateTimes();
});
setInterval(updateTimes, 60000);


// ========================================
// REPLY SYSTEM & COMMENT HANDLING
// ========================================

// 1. Toggle Reply Form
function toggleReplyForm(commentId) {
    const formContainer = document.getElementById(`reply-form-container-${commentId}`);
    if (formContainer) {
        formContainer.classList.toggle('hidden');
    }
}

// 1.1 Prepare Reply (Reply to explicit user)
function prepareReply(parentId, targetName, targetUserId) {
    const formContainer = document.getElementById(`reply-form-container-${parentId}`);
    if (formContainer) {
        formContainer.classList.remove('hidden');
        const form = formContainer.querySelector('form');
        const input = form.querySelector('input[name="content"]');

        // Pre-fill mention
        input.value = `@${targetName} `;
        input.focus();

        // Store target user ID
        form.dataset.targetUserId = targetUserId;
    }
}

// 2. Handle Reply Form Submission (Enter key or Button)
document.addEventListener('submit', async function (e) {
    if (e.target.matches('.reply-form')) {
        e.preventDefault();
        e.stopImmediatePropagation(); // Prevent other listeners if duplicate

        const form = e.target;

        // Anti-double submission guard
        if (form.dataset.submitting === "true") return;
        form.dataset.submitting = "true";

        const btn = form.querySelector('button[type="submit"]');

        const postId = form.dataset.postId;
        const parentId = form.dataset.parentId;
        const input = form.querySelector('input[name="content"]');
        const content = input.value;

        if (!content || !content.trim()) {
            form.dataset.submitting = "false";
            return;
        }

        // Check if we have a target user stored
        let targetUserId = form.dataset.targetUserId || null;

        if (btn) btn.disabled = true; // Disable button

        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const response = await fetch('/Post/Comment', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    postId: parseInt(postId),
                    content: content,
                    parentCommentId: parseInt(parentId),
                    targetUserId: targetUserId ? parseInt(targetUserId) : null
                })
            });

            if (response.ok) {
                input.value = '';
                delete form.dataset.targetUserId; // Clear target
                toggleReplyForm(parentId);
                reloadComments(postId);
            } else {
                console.error('Failed to reply');
            }
        } catch (error) {
            console.error(error);
        } finally {
            form.dataset.submitting = "false";
            if (btn) btn.disabled = false; // Enable button
        }
    }
});

// 3. Reload Comments Partial
async function reloadComments(postId) {
    try {
        const res = await fetch(`/Post/GetComments?postId=${postId}`);
        if (res.ok) {
            const html = await res.text();

            // Try updating standard index/feed wrapper
            const section = document.getElementById(`comment-section-${postId}`);
            if (section) {
                // Let's try to find the container.
                // For Index: it has class 'custom-scrollbar'
                let container = section.querySelector('.custom-scrollbar');
                if (!container) {
                    // For Profile (maybe): it has class 'mt-3 space-y-3'
                    container = section.querySelector('.mt-3.space-y-3');
                }

                if (container) {
                    container.innerHTML = html;
                    if (typeof updateTimes === 'function') {
                        updateTimes();
                    }
                }
            }

            // Optionally update count if we can parse it or fetch it separately.
            updateCommentCount(postId);
        }
    } catch (e) {
        console.error(e);
    }
}

// Helper to update count
async function updateCommentCount(postId) {
    try {
        // Placeholder for future count update logic
    } catch (e) { }
}


// 4. Handle Main Comment Form Submission (delegated)
document.addEventListener('submit', async function (e) {
    if (e.target.matches('.comment-form')) {
        e.preventDefault();
        const form = e.target;
        const postId = form.dataset.postId;
        const input = form.querySelector('input[name="content"]');
        const content = input.value;
        const btn = form.querySelector('button[type="submit"]');

        if (!content || !content.trim()) return;

        if (btn) btn.disabled = true; // Disable button

        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const response = await fetch('/Post/Comment', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({ postId: parseInt(postId), content })
            });

            if (response.ok) {
                input.value = '';
                reloadComments(postId);
            }
        } catch (error) {
            console.error(error);
        } finally {
            if (btn) btn.disabled = false; // Enable button
        }
    }
});

// 5. Handle Delete Comment (delegated)
document.addEventListener('click', function (e) {
    if (e.target.matches('.delete-comment-btn')) {
        const btn = e.target;
        const commentId = btn.dataset.commentId;
        const postId = btn.dataset.postId;

        showGlobalConfirm(
            "Xóa bình luận",
            "Bạn có chắc muốn xóa bình luận này?",
            async () => {
                try {
                    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                    const response = await fetch('/Post/DeleteComment', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': token
                        },
                        body: JSON.stringify(parseInt(commentId))
                    });

                    if (response.ok) {
                        reloadComments(postId);
                    }
                } catch (error) {
                    console.error(error);
                }
            }
        );
    }
});


// Helper for Global Modal
function showGlobalConfirm(title, message, onConfirm) {
    const modal = document.getElementById("globalConfirmationModal");
    if (!modal) {
        // Fallback if modal missing
        if (confirm(message)) onConfirm();
        return;
    }
    document.getElementById("globalModalTitle").innerText = title;
    document.getElementById("globalModalMessage").innerText = message;

    const confirmBtn = document.getElementById("globalModalConfirm");
    const cancelBtn = document.getElementById("globalModalCancel");

    // Clean up old listeners by cloning
    const newConfirm = confirmBtn.cloneNode(true);
    confirmBtn.parentNode.replaceChild(newConfirm, confirmBtn);

    const newCancel = cancelBtn.cloneNode(true);
    cancelBtn.parentNode.replaceChild(newCancel, cancelBtn);

    newConfirm.addEventListener("click", () => {
        onConfirm();
        modal.classList.add("hidden");
    });

    newCancel.addEventListener("click", () => {
        modal.classList.add("hidden");
    });

    // Close on outside click
    modal.onclick = (e) => {
        if (e.target === modal) modal.classList.add("hidden");
    };

    modal.classList.remove("hidden");
}
