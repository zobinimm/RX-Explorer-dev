namespace SharedLibrary
{
    public enum IOPreference
    {
        NoPreference,
        PreferUseMoreMemory,
    }

    public enum AccountType
    {
        Group,
        User,
        Unknown
    }

    public enum Permissions
    {
        FullControl,
        Modify,
        ListDirectory,
        ReadAndExecute,
        Read,
        Write
    }

    public enum CreateType
    {
        File,
        Folder
    }

    public enum CollisionOptions
    {
        None,
        Skip,
        RenameOnCollision,
        OverrideOnCollision
    }

    public enum ModifyAttributeAction
    {
        Add,
        Remove
    }

    public enum WindowState
    {
        Normal = 0,
        Minimized = 1,
        Maximized = 2
    }

    public enum AccessMode
    {
        Read,
        Write,
        ReadWrite,
        Exclusive
    }

    public enum OptimizeOption
    {
        None,
        Sequential,
        RandomAccess,
    }

    public enum MonitorCommandType
    {
        SetRecoveryData,
        RegisterRestartRequest,
        StartMonitor,
        StopMonitor,
        EnableFeature,
        DisableFeature
    }

    public enum MonitorFeature
    {
        CrashMonitor,
        FreezeMonitor
    }

    public enum AuxiliaryTrustProcessCommandType
    {
        Test,
        GetFriendlyTypeName,
        GetPermissions,
        SetDriveLabel,
        SetDriveIndexStatus,
        GetDriveIndexStatus,
        SetDriveCompressionStatus,
        GetDriveCompressionStatus,
        DetectEncoding,
        GetAllEncodings,
        RunExecutable,
        ToggleQuicklookWindow,
        SwitchQuicklookWindow,
        CheckQuicklookAvailable,
        CloseQuicklookWindow,
        CheckQuicklookWindowVisible,
        ToggleSeerWindow,
        SwitchSeerWindow,
        CheckSeerAvailable,
        CloseSeerWindow,
        CheckSeerWindowVisible,
        GetAssociation,
        Default_Association,
        GetRecycleBinItems,
        RestoreRecycleItem,
        DeleteRecycleItem,
        InterceptWinE,
        InterceptFolder,
        RestoreWinEInterception,
        RestoreFolderInterception,
        GetLinkData,
        GetUrlData,
        CreateNew,
        Copy,
        Move,
        Delete,
        Rename,
        EmptyRecycleBin,
        UnlockOccupy,
        EjectUSB,
        GetVariablePath,
        CreateLink,
        UpdateLink,
        UpdateUrl,
        PasteRemoteFile,
        GetContextMenuItems,
        InvokeContextMenuItem,
        CheckIfEverythingAvailable,
        SearchByEverything,
        GetThumbnail,
        SetFileAttribute,
        GetMIMEContentType,
        GetAllInstalledUwpApplication,
        CheckPackageFamilyNameExist,
        GetSpecificInstalledUwpApplication,
        GetDocumentProperties,
        LaunchUWP,
        GetThumbnailOverlay,
        SetAsTopMostWindow,
        RemoveTopMostWindow,
        GetNativeHandle,
        GetTooltipText,
        GetVariablePathList,
        GetDirectoryMonitorHandle,
        MapToUncPath,
        SetTaskBarProgress,
        GetProperties,
        MTPGetItem,
        MTPCheckExists,
        MTPGetChildItems,
        MTPGetDriveVolumnData,
        MTPCreateSubItem,
        MTPDownloadAndGetHandle,
        MTPReplaceWithNewFile,
        OrderByNaturalStringSortAlgorithm,
        GetSizeOnDisk,
        GetAvailableWslDrivePathList,
        GetRemoteClipboardRelatedData,
        CreateTemporaryFileHandle,
        ConvertToLongPath,
        GetRecyclePathFromOriginPath,
        GetFileAttribute,
        GetProcessHandle,
        SetWallpaperImage,
        GetAvailableNetworkPort,
        RetrieveAADTokenFromBackend,
        RedeemCodeFromBackend
    }

    public enum MenuFlags : uint
    {
        /// <summary>
        /// Indicates that the uPosition parameter gives the identifier of the menu item. The MF_BYCOMMAND flag is the default if neither
        /// the MF_BYCOMMAND nor MF_BYPOSITION flag is specified.
        /// </summary>
        MF_BYCOMMAND = 0x00000000,

        /// <summary>
        /// Indicates that the uPosition parameter gives the zero-based relative position of the new menu item. If uPosition is -1, the
        /// new menu item is appended to the end of the menu.
        /// </summary>
        MF_BYPOSITION = 0x00000400,

        /// <summary>
        /// Draws a horizontal dividing line. This flag is used only in a drop-down menu, submenu, or shortcut menu. The line cannot be
        /// grayed, disabled, or highlighted. The lpNewItem and uIDNewItem parameters are ignored.
        /// </summary>
        MF_SEPARATOR = 0x00000800,

        /// <summary>Enables the menu item so that it can be selected, and restores it from its grayed state.</summary>
        MF_ENABLED = 0x00000000,

        /// <summary>Disables the menu item and grays it so that it cannot be selected.</summary>
        MF_GRAYED = 0x00000001,

        /// <summary>Disables the menu item so that it cannot be selected, but the flag does not gray it.</summary>
        MF_DISABLED = 0x00000002,

        /// <summary>
        /// Does not place a check mark next to the item (default). If the application supplies check-mark bitmaps (see
        /// SetMenuItemBitmaps), this flag displays the clear bitmap next to the menu item.
        /// </summary>
        MF_UNCHECKED = 0x00000000,

        /// <summary>
        /// Places a check mark next to the menu item. If the application provides check-mark bitmaps (see SetMenuItemBitmaps, this flag
        /// displays the check-mark bitmap next to the menu item.
        /// </summary>
        MF_CHECKED = 0x00000008,

        /// <summary>Undocumented.</summary>
        MF_USECHECKBITMAPS = 0x00000200,

        /// <summary>Specifies that the menu item is a text string; the lpNewItem parameter is a pointer to the string.</summary>
        MF_STRING = 0x00000000,

        /// <summary>Uses a bitmap as the menu item. The lpNewItem parameter contains a handle to the bitmap.</summary>
        MF_BITMAP = 0x00000004,

        /// <summary>
        /// Specifies that the item is an owner-drawn item. Before the menu is displayed for the first time, the window that owns the
        /// menu receives a WM_MEASUREITEM message to retrieve the width and height of the menu item. The WM_DRAWITEM message is then
        /// sent to the window procedure of the owner window whenever the appearance of the menu item must be updated.
        /// </summary>
        MF_OWNERDRAW = 0x00000100,

        /// <summary>
        /// Specifies that the menu item opens a drop-down menu or submenu. The uIDNewItem parameter specifies a handle to the drop-down
        /// menu or submenu. This flag is used to add a menu name to a menu bar, or a menu item that opens a submenu to a drop-down menu,
        /// submenu, or shortcut menu.
        /// </summary>
        MF_POPUP = 0x00000010,

        /// <summary>
        /// Functions the same as the MF_MENUBREAK flag for a menu bar. For a drop-down menu, submenu, or shortcut menu, the new column
        /// is separated from the old column by a vertical line.
        /// </summary>
        MF_MENUBARBREAK = 0x00000020,

        /// <summary>
        /// Places the item on a new line (for a menu bar) or in a new column (for a drop-down menu, submenu, or shortcut menu) without
        /// separating columns.
        /// </summary>
        MF_MENUBREAK = 0x00000040,

        /// <summary>Removes highlighting from the menu item.</summary>
        MF_UNHILITE = 0x00000000,

        /// <summary>Highlights the menu item. If this flag is not specified, the highlighting is removed from the item.</summary>
        MF_HILITE = 0x00000080,

        /// <summary>Undocumented.</summary>
        MF_DEFAULT = 0x00001000,

        /// <summary>
        /// Item is contained in the window menu. The lParam parameter contains a handle to the menu associated with the message.
        /// </summary>
        MF_SYSMENU = 0x00002000,

        /// <summary>Indicates that the menu item has a vertical separator to its left.</summary>
        MF_HELP = 0x00004000,

        /// <summary>Indicates that the menu item has a vertical separator to its left.</summary>
        MF_RIGHTJUSTIFY = 0x00004000,

        /// <summary>Item is selected with the mouse.</summary>
        MF_MOUSESELECT = 0x00008000,

        /// <summary>Undocumented.</summary>
        MF_END = 0x00000080,
    }

    public enum MenuItemType : uint
    {
        /// <summary>
        /// Displays the menu item using a text string. The dwTypeData member is the pointer to a null-terminated string, and the cch
        /// member is the length of the string.
        /// <para>MFT_STRING is replaced by MIIM_STRING.</para>
        /// </summary>
        MFT_STRING = MenuFlags.MF_STRING,

        /// <summary>
        /// Displays the menu item using a bitmap. The low-order word of the dwTypeData member is the bitmap handle, and the cch member
        /// is ignored.
        /// <para>MFT_BITMAP is replaced by MIIM_BITMAP and hbmpItem.</para>
        /// </summary>
        MFT_BITMAP = MenuFlags.MF_BITMAP,

        /// <summary>
        /// Places the menu item on a new line (for a menu bar) or in a new column (for a drop-down menu, submenu, or shortcut menu). For
        /// a drop-down menu, submenu, or shortcut menu, a vertical line separates the new column from the old.
        /// </summary>
        MFT_MENUBARBREAK = MenuFlags.MF_MENUBARBREAK,

        /// <summary>
        /// Places the menu item on a new line (for a menu bar) or in a new column (for a drop-down menu, submenu, or shortcut menu). For
        /// a drop-down menu, submenu, or shortcut menu, the columns are not separated by a vertical line.
        /// </summary>
        MFT_MENUBREAK = MenuFlags.MF_MENUBREAK,

        /// <summary>
        /// Assigns responsibility for drawing the menu item to the window that owns the menu. The window receives a WM_MEASUREITEM
        /// message before the menu is displayed for the first time, and a WM_DRAWITEM message whenever the appearance of the menu item
        /// must be updated. If this value is specified, the dwTypeData member contains an application-defined value.
        /// </summary>
        MFT_OWNERDRAW = MenuFlags.MF_OWNERDRAW,

        /// <summary>
        /// Displays selected menu items using a radio-button mark instead of a check mark if the hbmpChecked member is NULL.
        /// </summary>
        MFT_RADIOCHECK = 0x00000200,

        /// <summary>
        /// Specifies that the menu item is a separator. A menu item separator appears as a horizontal dividing line. The dwTypeData and
        /// cch members are ignored. This value is valid only in a drop-down menu, submenu, or shortcut menu.
        /// </summary>
        MFT_SEPARATOR = MenuFlags.MF_SEPARATOR,

        /// <summary>
        /// Specifies that menus cascade right-to-left (the default is left-to-right). This is used to support right-to-left languages,
        /// such as Arabic and Hebrew.
        /// </summary>
        MFT_RIGHTORDER = 0x00002000,

        /// <summary>
        /// Right-justifies the menu item and any subsequent items. This value is valid only if the menu item is in a menu bar.
        /// </summary>
        MFT_RIGHTJUSTIFY = MenuFlags.MF_RIGHTJUSTIFY,
    }
}
