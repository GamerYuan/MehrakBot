#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Models;

#endregion

namespace MehrakCore.Services.Commands;

public interface IRealTimeNotesApiService<T> : IApiService where T : IRealTimeNotesData
{
    Task<ApiResult<T>> GetRealTimeNotesAsync(string roleId, string server, ulong ltuid, string ltoken);
}