using System;

namespace LibCK3
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class CK3SaveGameVersionAttribute : Attribute
    {
        public string SaveGameVersion { get; }

        public CK3SaveGameVersionAttribute(string saveGameVersion)
        {
            this.SaveGameVersion = saveGameVersion;
        }
    }
}
