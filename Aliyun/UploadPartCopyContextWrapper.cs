using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TransferLib.Aliyun
{
    public class UploadPartCopyContextWrapper
    {
        public UploadPartCopyContext Context { get; set; }
        public int PartNumber { get; set; }

        public UploadPartCopyContextWrapper(UploadPartCopyContext context, int partNumber)
        {
            Context = context;
            PartNumber = partNumber;
        }
    }
}
