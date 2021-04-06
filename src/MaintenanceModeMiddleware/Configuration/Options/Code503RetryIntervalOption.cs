﻿namespace MaintenanceModeMiddleware.Configuration.Options
{
    internal class Code503RetryIntervalOption : Option<int>
    {
        public override void FromString(string str)
        {
            Value = int.Parse(str);
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }
}
