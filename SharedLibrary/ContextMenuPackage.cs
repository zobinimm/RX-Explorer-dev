﻿using System;

namespace SharedLibrary
{
    public sealed class ContextMenuPackage : ICloneable
    {
        public string Name { get; set; }

        public int Id { get; set; }

        public string Verb { get; set; }

        public byte[] IconData { get; set; }

        public bool IncludeExtensionItem { get; set; }

        public string[] RelatedPath { get; set; }

        public MenuItemType MenuType { get; set; }

        public ContextMenuPackage[] SubMenus { get; set; }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}
