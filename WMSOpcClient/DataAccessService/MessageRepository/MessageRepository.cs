﻿using System;
using System.Collections.Generic;
using System.Text;
using TableDependency.SqlClient;
using TableDependency.SqlClient.Base.EventArgs;
using WMSOpcClient.DataAccessService.Models;

namespace WMSOpcClient.DataAccessService.MessageRepository
{
    public delegate void NewMessageHandler(MessageModel message);
    public class MessageRepository : IDisposable, IMessageRepository
    {
        public event NewMessageHandler OnNewMessage;

        private SqlTableDependency<MessageModel> _tableDependency;
        public void Start(string connectionString)
        {
            _tableDependency = new SqlTableDependency<MessageModel>(connectionString, "aBox");
            _tableDependency.OnChanged += DataChanged;
            _tableDependency.Start();
        }


        private void DataChanged(object sender, RecordChangedEventArgs<MessageModel> e)
        {
            if (e.ChangeType == TableDependency.SqlClient.Base.Enums.ChangeType.Insert || e.ChangeType == TableDependency.SqlClient.Base.Enums.ChangeType.Update)
            {
                OnNewMessage?.Invoke(e.Entity);
            }
        }

        private bool disposedValue = false;
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing && _tableDependency != null)
                {
                    _tableDependency.Stop();
                    _tableDependency.Dispose();
                }

                disposedValue = true;
            }
        }
    }
}
