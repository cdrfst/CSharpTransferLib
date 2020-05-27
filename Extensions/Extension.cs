using System;
using System.Text;

namespace TransferLib.Extensions
{
    public static class Extension
    {
        /// <summary>
        /// 返回异常消息
        /// </summary>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static string GetErrorString(this Exception exp)
        {
            var builder = new StringBuilder(100);
            Exception tmp = exp;
            do
            {
                builder.AppendLine(tmp.Message);
                tmp = tmp.InnerException;
            } while (tmp != null);
            return builder.ToString();
        }
    }
}
