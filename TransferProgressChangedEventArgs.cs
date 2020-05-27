using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace TransferLib
{
    public class TransferProgressChangedEventArgs
    {
        public int ProgressPercentage { get; private set; }
        public string Speed { get; private set; }

        public TransferProgressChangedEventArgs(int progressPercentage, string speed)
        {
            ProgressPercentage = progressPercentage;
            Speed = speed;
        }
    }
}
