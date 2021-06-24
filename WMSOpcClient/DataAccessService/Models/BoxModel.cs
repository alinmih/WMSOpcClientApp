﻿using System;
using System.Collections.Generic;
using System.Text;

namespace WMSOpcClient.DataAccessService.Models
{
    public class BoxModel
    {
        public int Id { get; set; }
        public string SSCC { get; set; }
        public bool OriginalBox { get; set; }
        public int Destination { get; set; }
        public int SendToServer { get; set; }
        public string PickingLocation { get; set; }

    }
}
