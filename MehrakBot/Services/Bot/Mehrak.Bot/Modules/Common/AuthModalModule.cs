#region

using Mehrak.Bot.Authentication;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Metrics;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

#endregion

namespace Mehrak.Bot.Modules.Common;

public class AuthModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    public static ModalProperties AddAuthModal => new ModalProperties("add_auth_modal", "Authenticate")
        .WithComponents([
            new LabelProperties("HoYoLAB UID", new TextInputProperties("ltuid", TextInputStyle.Short)),
            new LabelProperties("HoYoLAB Cookies", new TextInputProperties("ltoken", TextInputStyle.Paragraph)),
            new LabelProperties("Passphrase", new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                .WithPlaceholder("Do not use the same password as your Discord or HoYoLAB account!").WithMaxLength(64))
        ]);

    public static ModalProperties AuthModal(string guid)
    {
        return new ModalProperties($"auth_modal:{guid}", "Authenticate")
            .AddComponents(
                new LabelProperties("Passphrase", new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                    .WithPlaceholder("Your Passphrase").WithMaxLength(64))
            );
    }

    public static ModalProperties UpdateAuthModal(UserProfileDto profile)
    {
        return new ModalProperties($"update_auth_modal:{profile.ProfileId}", "Update Authentication")
            .WithComponents([
                new TextDisplayProperties($"## Profile {profile.ProfileId}\n### HoYoLAB UID: {profile.LtUid}"),
                new LabelProperties("HoYoLAB Cookies", new TextInputProperties("ltoken", TextInputStyle.Paragraph)),
                new LabelProperties("Passphrase", new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                    .WithPlaceholder("Do not use the same password as your Discord or HoYoLAB account!").WithMaxLength(64))
            ]);

    }

    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly IEncryptionService m_CookieService;
    private readonly UserDbContext m_UserContext;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;
    private readonly BotMetricsService m_Metrics;

    public AuthModalModule(
        IEncryptionService cookieService,
        UserDbContext userRepository,
        IAuthenticationMiddlewareService authenticationMiddleware,
        BotMetricsService metrics,
        ILogger<AuthModalModule> logger)
    {
        m_Logger = logger;
        m_CookieService = cookieService;
        m_UserContext = userRepository;
        m_AuthenticationMiddleware = authenticationMiddleware;
        m_Metrics = metrics;
    }

    [ComponentInteraction("add_auth_modal")]
    public async Task AddAuth()
    {
        try
        {
            m_Logger.LogInformation("Processing add auth modal submission from user {UserId}", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));

            var user = await m_UserContext.Users
                .Where(u => u.Id == (long)Context.User.Id)
                .Include(u => u.Profiles)
                .SingleOrDefaultAsync();
            if (user == null)
            {
                user = new UserModel
                {
                    Id = (long)Context.User.Id,
                    Timestamp = DateTime.UtcNow,
                    Profiles = []
                };
                m_Metrics.AdjustUniqueUserCount(1);
                await m_UserContext.Users.AddAsync(user);
            }

            var inputs = Context.Components
                .OfType<Label>()
                .Select(l => l.Component)
                .OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            if (!ulong.TryParse(inputs["ltuid"], out var ltuid))
            {
                m_Logger.LogWarning("User {UserId} provided invalid UID format", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Invalid UID!")));
                return;
            }

            m_Logger.LogDebug("Encrypting cookie for user {UserId}", Context.User.Id);
            if (user.Profiles.Any(x => x.LtUid == (long)ltuid))
            {
                m_Logger.LogWarning("User {UserId} already has a profile with UID {LtUid}", Context.User.Id, ltuid);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Profile already exists!")));
                return;
            }

            try
            {
                await m_UserContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                m_Logger.LogError(e, "Failed to add profile for user {UserId}", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Failed to add profile! Please try again later")));
                return;
            }

            UserProfileModel profile = new()
            {
                UserId = (long)Context.User.Id,
                ProfileId = user.Profiles.Count + 1,
                LtUid = (long)ltuid,
                LToken = await Task.Run(() =>
                    m_CookieService.Encrypt(inputs["ltoken"], inputs["passphrase"]))
            };

            await m_UserContext.UserProfiles.AddAsync(profile);
            try
            {
                await m_UserContext.SaveChangesAsync();
                m_Logger.LogInformation("User {UserId} added new profile", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Added profile successfully!")));
            }
            catch (DbUpdateException e)
            {
                m_Logger.LogError(e, "Failed to add profile for user {UserId}", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Failed to add profile! Please try again later")));
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("An error occurred while processing your request. Please try again later")));
        }
    }


    [ComponentInteraction("update_auth_modal")]
    public async Task UpdateModalCallback(uint profileId)
    {
        try
        {
            m_Logger.LogInformation("Processing update auth modal submission from user {UserId}", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));

            var user = await m_UserContext.Users
                .AsNoTracking()
                .Where(u => u.Id == (long)Context.User.Id)
                .Select(u => new UserDto()
                {
                    Id = (ulong)u.Id,
                    Profiles = u.Profiles.Where(p => p.ProfileId == profileId)
                        .Select(p => new UserProfileDto()
                        {
                            Id = p.Id,
                            ProfileId = p.ProfileId,
                            LtUid = (ulong)p.LtUid
                        }).ToList()
                }).FirstOrDefaultAsync();

            var profile = user?.Profiles?.FirstOrDefault();

            if (profile == null)
            {
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("No profile found!")));
                return;
            }

            var inputs = Context.Components
                .OfType<Label>()
                .Select(l => l.Component)
                .OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            var newLToken = await Task.Run(() => m_CookieService.Encrypt(inputs["ltoken"], inputs["passphrase"]));

            try
            {
                await m_UserContext.UserProfiles
                    .Where(p => p.Id == profile.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LToken, newLToken));
            }
            catch (DbUpdateException e)
            {
                m_Logger.LogError(e, "Failed to update profile for user {UserId}", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Failed to update profile! Please try again later")));
                return;
            }

            await m_AuthenticationMiddleware.RevokeAuthenticate(Context.User.Id, profile.LtUid);
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("Profile successfully updated!")));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing update auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("An error occurred while processing your request. Please try again later")));
        }
    }


    [ComponentInteraction("auth_modal")]
    public async Task AuthModalCallback(string guid)
    {
        var passphrase = Context.Components
            .OfType<Label>()
            .Select(l => l.Component)
            .OfType<TextInput>()
            .First(x => x.CustomId == "passphrase").Value;

        if (!m_AuthenticationMiddleware.NotifyAuthenticate(new AuthenticationResponse(Context.User.Id, guid, passphrase,
                Context)))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(
                    MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(
                    new TextDisplayProperties(
                        "This authentication request has expired or is invalid. Please try again"))));
        }
    }
}
