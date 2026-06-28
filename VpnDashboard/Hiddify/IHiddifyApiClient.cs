using VpnDashboard.Data.Entities;
using VpnDashboard.Hiddify.Dtos;

namespace VpnDashboard.Hiddify;

public interface IHiddifyApiClient
{
    Task<bool> PingAsync(HiddifyServer server, CancellationToken ct = default);

    Task<HiddifyResult<List<HiddifyUserDto>>> GetUsersAsync(HiddifyServer server, CancellationToken ct = default);

    Task<HiddifyResult<HiddifyUserDto>> CreateUserAsync(HiddifyServer server, CreateHiddifyUserRequest request, CancellationToken ct = default);

    Task<HiddifyResult<HiddifyUserDto>> UpdateUserAsync(HiddifyServer server, string uuid, object patch, CancellationToken ct = default);

    Task<bool> DeleteUserAsync(HiddifyServer server, string uuid, CancellationToken ct = default);
}
