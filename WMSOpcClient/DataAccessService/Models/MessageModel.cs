using System;
using System.Collections.Generic;
using System.Text;

namespace WMSOpcClient.DataAccessService.Models
{
    public class MessageModel
    {
        public int Id { get; set; }
        public string SSSC { get; set; }
        public bool OriginalBox { get; set; }
        public int Destination { get; set; }
    }
}
