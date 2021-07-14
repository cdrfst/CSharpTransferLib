using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace TransferLib
{
    public class MyWebClient:WebClient
    {
        /// <summary>
        /// 过期时间
        /// </summary>
        public int Timeout { get; set; }
        public MyWebClient(int timeout = 30000)
        {   //默认30秒
            Timeout = timeout;
        }
        protected override WebRequest GetWebRequest(Uri address)
        {
            HttpWebRequest request = (HttpWebRequest)base.GetWebRequest(address);
            request.Timeout = Timeout;
            request.ReadWriteTimeout = Timeout;
            return request;
        }
    }
}
