using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Domain;
using Mehrak.Domain.Common;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Hi3.Types;

namespace Mehrak.Application.Services.Hi3;

internal static class Hi3ApplicationServiceExtensions
{
    public static IServiceCollection AddHi3ApplicationServices(this IServiceCollection services)
    {
        services.AddKeyedTransient<IApplicationService, Hi3CharacterApplicationService>(CommandName.Hi3.Character);
        services.AddSingleton<ICardService<Hi3CharacterDetail>, Hi3CharacterCardService>();

        services.RegisterAsyncInitializableFor<
            ICardService<Hi3CharacterDetail>, Hi3CharacterCardService>();

        return services;
    }
}
