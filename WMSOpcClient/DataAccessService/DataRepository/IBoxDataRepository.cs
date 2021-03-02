using System.Collections.Generic;
using System.Threading.Tasks;
using WMSOpcClient.DataAccessService.Models;

namespace WMSOpcClient.DataAccessService.DataRepository
{
    public interface IBoxDataRepository
    {
        Task<List<BoxModel>> GetBoxes();
        Task<int> UpdateBoxes(List<BoxModel> boxes);
        Task<int> UpdateServerReceived(BoxModel boxModel);
        Task<int> UpdateSentToServer(BoxModel boxModel);
        bool IsSQLServerConnected(string connectionString);
    }
}