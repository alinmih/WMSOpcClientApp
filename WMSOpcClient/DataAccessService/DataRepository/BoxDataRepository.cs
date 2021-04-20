using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;
using WMSOpcClient.DataAccessService.Models;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace WMSOpcClient.DataAccessService.DataRepository
{
    public class BoxDataRepository : IBoxDataRepository
    {
        private readonly ConnectionStringData _connectionString;
        private readonly IConfiguration _configuration;

        public BoxDataRepository(IConfiguration configuration, ConnectionStringData connectionString)
        {
            _configuration = configuration;
            _connectionString = connectionString;
        }

        /// <summary>
        /// Get all the boxes from database
        /// </summary>
        /// <returns></returns>
        public async Task<List<BoxModel>> GetBoxes()
        {
            using (IDbConnection dbConnection = new SqlConnection(_configuration.GetConnectionString(_connectionString.SqlConnectionName)))
            {
                var sqlString = "SELECT[Id]\n"
                      + "      ,[SSCC]\n"
                      + "      ,[OriginalBox]\n"
                      + "      ,[Destination]\n"
                      + "      ,[SendToServer]\n"
                      + "  FROM [dbo].[SEN_Conveyor_Box]\n"
                      + "  WHERE dbo.SEN_Conveyor_Box.SendToServer=0 OR dbo.SEN_Conveyor_Box.SendToServer is NULL";
                var records = await dbConnection.QueryAsync<BoxModel>(sqlString);

                return records.ToList();
            }

        }

        /// <summary>
        /// Update table with the sent boxes
        /// </summary>
        /// <param name="boxes"></param>
        /// <returns></returns>
        public async Task<int> UpdateBoxes(List<BoxModel> boxes)
        {
            using (IDbConnection dbConnection = new SqlConnection(_configuration.GetConnectionString(_connectionString.SqlConnectionName)))
            {
                var affectedRows = 0;
                foreach (var box in boxes)
                {
                    var sqlString = "UPDATE [dbo].[SEN_Conveyor_Box]\n"
                           + "   SET [SendToServer] = 1\n"
                           + $" WHERE [dbo].[SEN_Conveyor_Box].Id = {box.Id}";
                    var affectedRow = await dbConnection.ExecuteAsync(sqlString);
                    if (affectedRow == 1)
                    {
                        box.SendToServer = 1;
                    }
                    affectedRows += affectedRow;
                }

                return affectedRows;
            }

        }

        /// <summary>
        /// Update box record with send ack
        /// </summary>
        /// <param name="boxModel"></param>
        /// <returns></returns>
        public async Task<int> UpdateSSCCRead(string sscc)
        {
            using (IDbConnection dbConnection = new SqlConnection(_configuration.GetConnectionString(_connectionString.SqlConnectionName)))
            {

                var sqlString = "UPDATE [dbo].[SEN_Conveyor_Box]\n"
                       + $"   SET [SSCCRead] = 1\n"
                       + $" WHERE [dbo].[SEN_Conveyor_Box].SSCC = '{sscc}'";
                var affectedRow = await dbConnection.ExecuteAsync(sqlString);

                return affectedRow;
            }
        }

        public async Task<int> UpdateSentToServer(BoxModel boxModel)
        {
            using (IDbConnection dbConnection = new SqlConnection(_configuration.GetConnectionString(_connectionString.SqlConnectionName)))
            {

                var sqlString = "UPDATE [dbo].[SEN_Conveyor_Box]\n"
                       + $"   SET [SendToServer] = 1\n"
                       + $" WHERE [dbo].[SEN_Conveyor_Box].Id = {boxModel.Id}";
                var affectedRow = await dbConnection.ExecuteAsync(sqlString);
                if (affectedRow == 1)
                {
                    boxModel.SendToServer = 1;
                }
                return affectedRow;
            }
        }

        /// <summary>
        /// Check if SQL server online
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public bool IsSQLServerConnected(string connectionString)
        {
            try
            {
                using (IDbConnection dbConnection = new SqlConnection(connectionString))
                {
                    dbConnection.Open();
                    return true;
                }
            }
            catch (SqlException)
            {
                return false;
            }
        }
    }
}
