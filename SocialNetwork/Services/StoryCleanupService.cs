using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SocialNetwork.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SocialNetwork.Services
{
    public class StoryCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _environment;

        public StoryCleanupService(IServiceScopeFactory scopeFactory, IWebHostEnvironment environment)
        {
            _scopeFactory = scopeFactory;
            _environment = environment;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<SocialNetworkDbContext>();
                        
                        // Find expired stories
                        var expiredStories = await context.Stories
                            .Where(s => s.ExpiresAt < DateTime.Now)
                            .ToListAsync(stoppingToken);

                        if (expiredStories.Any())
                        {
                            foreach (var story in expiredStories)
                            {
                                // Delete physical file
                                if (!string.IsNullOrEmpty(story.MediaUrl))
                                {
                                    try
                                    {
                                        // Trim leading slash if present
                                        var relativePath = story.MediaUrl.TrimStart('/');
                                        var fullPath = Path.Combine(_environment.WebRootPath, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));

                                        if (File.Exists(fullPath))
                                        {
                                            File.Delete(fullPath);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error deleting file for story {story.Id}: {ex.Message}");
                                    }
                                }
                            }

                            // Delete from DB
                            context.Stories.RemoveRange(expiredStories);
                            await context.SaveChangesAsync(stoppingToken);
                            
                            Console.WriteLine($"Cleaned up {expiredStories.Count} expired stories.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in StoryCleanupService: {ex.Message}");
                }

                // Wait 1 hour before next check
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
