using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard;

public static class ReleaseNoteSeedData
{
    public static async Task SeedReleaseNotesAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReleaseNoteDbContext>();

        if (await db.ReleaseVersions.AnyAsync())
            return;

        var releases = GetSeedData();
        db.ReleaseVersions.AddRange(releases);
        await db.SaveChangesAsync();
    }

    private static List<ReleaseVersionModel> GetSeedData()
    {
        return
        [
            new ReleaseVersionModel
            {
                Version = "v1.1.0",
                Date = "",
                DisplayOrder = 20,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "Updated website for public access" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[profile] Improved handling of profile update and profile delete. You can now select a profile with both the Profile ID and HoYoLAB UID" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[hyl] Added command. This command allows you to embed a HoYoLAB post in Discord by providing the post URL. It supports all languages that HoYoLAB ly supports" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[character] Fixed an issue whereby some character splash arts might be replaced by relic icons due to overlapping ID space" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[charlist] Fixed an issue whereby the level display incorrectly states \"AR\" instead of \"TB\"" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[pf] [as] Updated the layout for buff icons such that they are better centered" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[shiyu] Re-enabled command with support for Shiyu Defense V2." }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Behind the scenes",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "Added a source generator for generating help string snippets for commands" },
                            new ReleaseNoteEntry { Type = "feature", Text = "Added support for SeaweedFS Filer reverse proxy for Dashboard super admin users" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v1.0.0",
                Date = "",
                DisplayOrder = 19,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[help] Now displays the bot version" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[health] Now displays the bot version" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[profile] Added a new subcommand `update` to allow changing of Cookies and Passphrase without needing to delete and re-add the profile" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Updated rate limiting to allow for small burst of requests within a short period of time" },
                            new ReleaseNoteEntry { Type = "feature", Text = "Added Discord Rich Presence support to display registered user count" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added support for ascension level cap for characters" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added support for multiple card generation per command with comma-separated character names/aliases" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[notes] Fixed an issue whereby the command did not work" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[theater] Fixed an issue whereby the generated card might display multiple Arcana stages clear under certain circumstances" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added support for Elation characters" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added support for multiple card generation per command with comma-separated character names/aliases" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[notes] Fixed an issue whereby the command did not work" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[charlist] Added command" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[tower] Added command" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[shiyu] Temporarily disabled command to prepare for Shiyu Defense V2 support" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[da] Fixed an issue whereby the buff icons are not scaled properly" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[notes] Fixed an issue whereby the command did not work" },
                            new ReleaseNoteEntry { Type = "fix", Text = "Fixed an issue whereby character avatar icons may use skins for characters regardless of ownership" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HI3 Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[battlesuit] Fixed an issue whereby card generation will fail when one of the outfits owned does not exist on HoYoLAB API" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[battlesuit] Fixed an issue whereby the weapon name might overshoot the container width" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Behind the scenes",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "Migrated metrics server to ClickHouse" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Extracted core business logic as a standalone gRPC service" },
                            new ReleaseNoteEntry { Type = "feature", Text = "Added Dashboard website for maintainers" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.9.2",
                Date = "",
                DisplayOrder = 18,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added support for Honed Edge" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.9.1",
                Date = "",
                DisplayOrder = 17,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[help] Fixed an issue whereby `zzz da` and `zzz shiyu` is not listed as available commands in the base help command" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[character] Fixed an issue whereby Normal Attack talents enhanced by unlocked constellations are not properly highlighted" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.9.0",
                Date = "",
                DisplayOrder = 16,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "Fixed an issue whereby using a command without a profile does not return a valid response" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Add support for ascended image icons" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Highlight talent levels enhanced by unlocked constellations" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Add support to fetch HoYoWiki image from multiple locales" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Add support to auto update autocomplete list as users use the `character` or `charlist` commands" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[charlist] Add support for ascended image icons" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[aa] Add Anomaly Arbitration summary card" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Add support to fetch HoYoWiki image from multiple locales" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Add support to auto update autocomplete list as users use the `character` or `charlist` commands" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[notes] Add support for Currency Wars" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Add support to fetch HoYoWiki image from multiple locales" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Add support to auto update autocomplete list as users use the `character` command" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[da] Fixed an issue whereby summary card will fail to generate if duplicate buffs are selected" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HI3 Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[battlesuit] Add HI3 battlesuit summary card" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Behind the scenes",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "Upgrade to .NET 10" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Bump dependencies" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Migrated Database from MongoDB to SQL" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.8.0",
                Date = "",
                DisplayOrder = 15,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[checkin] Check in command now only checks in for games you have an account for" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[charlist] Fixed an issue whereby Sangonomiya Kokomi's name will exceed the bounds" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[character] Fixed an issue whereby Preservation characters' talent icons are incorrectly positioned" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Behind the scenes",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "Restructured codebase" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Better API logging" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Better metrics collection" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Bump dependencies" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.7.2",
                Date = "",
                DisplayOrder = 14,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[character] Fixed an issue whereby character cards will sometimes fail fetching splash art for card generation" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.7.1",
                Date = "",
                DisplayOrder = 13,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[help] Fixed an issue whereby new additions are not reflected in the help commands" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.7.0",
                Date = "",
                DisplayOrder = 12,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[checkin] Added support for Tears of Themis daily check-in" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[genshin] Added support for Imaginarium Theater Lunar Mode" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[shiyu] Added command" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[da] Added command" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[notes] Added command" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.6.1",
                Date = "",
                DisplayOrder = 11,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "Fixed a potential security issue with how token caches are stored and accessed. No instance of exploitation of this issue was discovered" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[character] Fixed an issue whereby card relic set names are not shown under certain circumstances" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.6.0",
                Date = "",
                DisplayOrder = 10,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[help] Updated to reflect new additions" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[character] Fixed an issue whereby the character `Candace` would display incorrect skill icons" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[charlist] Fixed an issue whereby the response does not have a `Remove` button" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[charlist] Added command" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[as][pf] Fixed an issue whereby displayed buff icons have incorrect size under certain circumstances" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added command" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Behind the Scenes",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "Reduced the cold start time for a command's first use since the bot's boot up" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.5.0",
                Date = "",
                DisplayOrder = 9,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[help] Updated to reflect new additions" },
                            new ReleaseNoteEntry { Type = "fix", Text = "Fixed an issue whereby authentication failure will send 2 error messages instead of 1" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[codes] Optimised the redemption time required" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[charlist] Added command" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[moc] Fixed an issue whereby the command throws an error for incomplete clears" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[codes] Optimised the redemption time" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[pf] Added command" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[as] Added command" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[codes] Optimised the redemption time" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.4.1",
                Date = "",
                DisplayOrder = 8,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[codes] Fixed an issue whereby multiple code redeems does not work due to short redemption interval" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[moc] Fixed an issue whereby Memory of Chaos card container shows incorrect timestamp" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[codes] Fixed an issue whereby multiple code redeems does not work due to short redemption interval" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[codes] Fixed an issue whereby multiple code redeems does not work due to short redemption interval" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.4.0",
                Date = "",
                DisplayOrder = 7,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[help] Updated to reflect new additions" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[health] Updated layout" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[abyss] Spiral Abyss summary card now fills up empty space for uncleared chambers" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[abyss] Fixed an issue whereby Spiral Abyss summary card shows incorrect cycle dates" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[codes] Command has been updated to redeem all known codes at once. Codes can still be manually specified and valid codes will be automatically added as known codes" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[stygian] Added command" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[moc] Added command" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[codes] Command has been updated to redeem all known codes at once. Codes can still be manually specified and valid codes will be automatically added as known codes" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "ZZZ Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[codes] Added command" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.3.0",
                Date = "",
                DisplayOrder = 6,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "[help] Updated to reflect new additions" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Reduced authentication time-out period to 1 minute" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Streamlined error messages" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "Better internal error logging" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added autocomplete for character names" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added aliasing for character names" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[character] Updated Cryo and Anemo background colour for character cards" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[character] Updated character card portrait image placement, such that most character portraits will be a better fit" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[theater] Added command" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Added aliasing for character names" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.2.5",
                Date = "",
                DisplayOrder = 5,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[character] Fixed an issue whereby the command fails prematurely due to missing Light Cone wiki entry from HoYoLAB API" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.2.4",
                Date = "",
                DisplayOrder = 4,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[abyss] Fixed an issue whereby the command will fail if the floor requested does not contain a character used on a different floor" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[abyss] Fixed an issue whereby abyss card generated does not have properly dimmed stars" },
                            new ReleaseNoteEntry { Type = "fix", Text = "[notes] Fixed an issue whereby they do not have the proper display text for when all expeditions/assignments are completed" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[notes] Fixed an issue whereby they do not have the proper display text for when all expeditions/assignments are completed" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.2.2",
                Date = "",
                DisplayOrder = 3,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "improvement", Text = "Added metrics for new commands" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "fix", Text = "[abyss] Fixed an issue whereby the command will fail if any character in Most Used Character list does not appear in the battles" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.2.1",
                Date = "",
                DisplayOrder = 2,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[health] Added command (Only available for server admins)" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[help] Updated to reflect the new additions" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[abyss] Added command" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[codes] Added command" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[notes] Added command" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[character] Dimmed flat stats for Artifacts" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[codes] Added command" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[notes] Added command" },
                            new ReleaseNoteEntry { Type = "improvement", Text = "[character] Dimmed flat stats for Relics" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ReleaseVersionModel
            {
                Version = "v0.1.1",
                Date = "",
                DisplayOrder = 1,
                Sections =
                [
                    new ReleaseNoteSection
                    {
                        Name = "Common",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[profile] Multi-profile support" },
                            new ReleaseNoteEntry { Type = "feature", Text = "[checkin] Added command" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "Genshin Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] Genshin Character Card" }
                        ]
                    },
                    new ReleaseNoteSection
                    {
                        Name = "HSR Toolbox",
                        Notes =
                        [
                            new ReleaseNoteEntry { Type = "feature", Text = "[character] HSR Character Card" }
                        ]
                    }
                ],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        ];
    }
}
