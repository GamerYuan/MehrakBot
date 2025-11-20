using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Application.Services.Hi3.Types;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Hi3.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Application.Services.Hi3;

internal static class Hi3ApplicationServiceExtensions
{
    public static IServiceCollection AddHi3ApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<IApplicationService<Hi3CharacterApplicationContext>, Hi3CharacterApplicationService>();
        services.AddSingleton<ICardService<Hi3CardGenerationContext<Hi3CharacterDetail>, Hi3CharacterDetail>, Hi3CharacterCardService>();

        return services;
    }
}
