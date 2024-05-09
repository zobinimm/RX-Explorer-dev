﻿using Microsoft.Toolkit.Deferred;
using Microsoft.Toolkit.Uwp.UI.Animations;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using RX_Explorer.Class;
using RX_Explorer.Dialog;
using RX_Explorer.Interface;
using RX_Explorer.SeparateWindow.PropertyWindow;
using SharedLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using CommandBarFlyout = Microsoft.UI.Xaml.Controls.CommandBarFlyout;
using TreeView = Microsoft.UI.Xaml.Controls.TreeView;
using TreeViewCollapsedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewCollapsedEventArgs;
using TreeViewExpandingEventArgs = Microsoft.UI.Xaml.Controls.TreeViewExpandingEventArgs;
using TreeViewItem = Microsoft.UI.Xaml.Controls.TreeViewItem;
using TreeViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs;
using TreeViewList = Microsoft.UI.Xaml.Controls.TreeViewList;
using TreeViewNode = Microsoft.UI.Xaml.Controls.TreeViewNode;

namespace RX_Explorer.View
{
    public sealed partial class FileControl : Page, IDisposable
    {
        private FilePresenter currentPresenter;
        private CommandBarFlyout ContextMenuFlyout;

        private readonly InterlockedNoReentryExecution SearchTextExecution = new InterlockedNoReentryExecution();
        private readonly InterlockedNoReentryExecution AddressTextExecution = new InterlockedNoReentryExecution();
        private readonly InterlockedNoReentryExecution AddressButtonExecution = new InterlockedNoReentryExecution();
        private readonly InterlockedNoReentryExecution CreateBladeExecution = new InterlockedNoReentryExecution();

        private readonly ObservableCollection<AddressBlock> AddressButtonList = new ObservableCollection<AddressBlock>();
        private readonly ObservableCollection<FileSystemStorageFolder> AddressExtensionList = new ObservableCollection<FileSystemStorageFolder>();
        private readonly ObservableCollection<AddressSuggestionItem> AddressSuggestionList = new ObservableCollection<AddressSuggestionItem>();
        private readonly ObservableCollection<SearchSuggestionItem> SearchSuggestionList = new ObservableCollection<SearchSuggestionItem>();
        private readonly ObservableCollection<NavigationRecordDisplay> NavigationRecordList = new ObservableCollection<NavigationRecordDisplay>();

        private readonly PointerEventHandler BladePointerPressedEventHandler;
        private readonly PointerEventHandler GoBackButtonPressedHandler;
        private readonly PointerEventHandler GoBackButtonReleasedHandler;
        private readonly PointerEventHandler GoForwardButtonPressedHandler;
        private readonly PointerEventHandler GoForwardButtonReleasedHandler;

        private CancellationTokenSource DelayEnterCancel;
        private CancellationTokenSource DelayGoBackHoldCancel;
        private CancellationTokenSource DelayGoForwardHoldCancel;
        private CancellationTokenSource ContextMenuCancellation;
        private CancellationTokenSource AddressExtensionCancellation;
        private CancellationTokenSource ExpandTreeViewCancellation;

        public TabItemContentRenderer Renderer { get; private set; }

        public bool ShouldNotAcceptShortcutKeyInput { get; set; }

        public FilePresenter CurrentPresenter
        {
            get => currentPresenter;
            set
            {
                if (currentPresenter != value)
                {
                    if (BladeViewer.Items.Count > 1)
                    {
                        if (currentPresenter != null)
                        {
                            currentPresenter.FocusIndicator.Background = new SolidColorBrush(Colors.Transparent);
                        }

                        if (value != null)
                        {
                            value.FocusIndicator.Background = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
                        }
                    }
                    else
                    {
                        if (currentPresenter != null)
                        {
                            currentPresenter.FocusIndicator.Background = new SolidColorBrush(Colors.Transparent);
                        }
                    }

                    currentPresenter = value;
                    TaskBarController.SetText(value?.CurrentFolder?.DisplayName);

                    if (value?.CurrentFolder is FileSystemStorageItemBase Folder)
                    {
                        RefreshAddressButton(Folder.Path);

                        GlobeSearch.PlaceholderText = $"{Globalization.GetString("SearchBox_PlaceholderText")} {Folder.DisplayName}";
                        GoParentFolder.IsEnabled = Folder.Path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ? !Folder.Path.Equals(Path.GetPathRoot(Folder.Path), StringComparison.OrdinalIgnoreCase) : Folder is not RootVirtualFolder;
                        GoBackRecord.IsEnabled = !value.BackNavigationStack.IsEmpty;
                        GoForwardRecord.IsEnabled = !value.ForwardNavigationStack.IsEmpty;

                        Renderer.TabItem.IconSource = new ImageIconSource { ImageSource = Folder.Thumbnail };

                        if (Renderer.TabItem.Header is TextBlock HeaderBlock)
                        {
                            HeaderBlock.Text = string.IsNullOrEmpty(Folder.DisplayName) ? $"<{Globalization.GetString("UnknownText")}>" : Folder.DisplayName;
                        }
                    }
                }
            }
        }

        public FileControl()
        {
            InitializeComponent();

            ContextMenuFlyout = CreateNewFolderContextMenu();

            BladePointerPressedEventHandler = new PointerEventHandler(Blade_PointerPressed);
            GoBackButtonPressedHandler = new PointerEventHandler(GoBackRecord_PointerPressed);
            GoBackButtonReleasedHandler = new PointerEventHandler(GoBackRecord_PointerReleased);
            GoForwardButtonPressedHandler = new PointerEventHandler(GoForwardRecord_PointerPressed);
            GoForwardButtonReleasedHandler = new PointerEventHandler(GoForwardRecord_PointerReleased);
            AppThemeController.Current.ThemeChanged += Current_ThemeChanged;

            AddressButtonContainer.RegisterPropertyChangedCallback(VisibilityProperty, new DependencyPropertyChangedCallback(OnAddressButtonContainerVisibiliyChanged));
        }

        private void Current_ThemeChanged(object sender, ElementTheme e)
        {
            ContextMenuFlyout = CreateNewFolderContextMenu();
        }

        private CommandBarFlyout CreateNewFolderContextMenu()
        {
            CommandBarFlyout Flyout = new CommandBarFlyout
            {
                AlwaysExpanded = true,
                ShouldConstrainToRootBounds = false
            };
            Flyout.Closing += RightTabFlyout_Closing;

            FontFamily FontIconFamily = Application.Current.Resources["SymbolThemeFontFamily"] as FontFamily;

            #region PrimaryCommand -> StandBarContainer
            AppBarButton CopyButton = new AppBarButton
            {
                Icon = new SymbolIcon { Symbol = Symbol.Copy },
                Name = "FolderCopyButton"
            };
            ToolTipService.SetToolTip(CopyButton, Globalization.GetString("Operate_Text_Copy"));
            CopyButton.Click += FolderCopy_Click;

            AppBarButton CutButton = new AppBarButton
            {
                Icon = new SymbolIcon { Symbol = Symbol.Cut },
                Name = "FolderCutButton"
            };
            ToolTipService.SetToolTip(CutButton, Globalization.GetString("Operate_Text_Cut"));
            CutButton.Click += FolderCut_Click;

            AppBarButton DeleteButton = new AppBarButton
            {
                Icon = new SymbolIcon { Symbol = Symbol.Delete },
                Name = "FolderDeleteButton"
            };
            ToolTipService.SetToolTip(DeleteButton, Globalization.GetString("Operate_Text_Delete"));
            DeleteButton.Click += FolderDelete_Click;

            AppBarButton RenameButton = new AppBarButton
            {
                Icon = new SymbolIcon { Symbol = Symbol.Rename },
                Name = "FolderRenameButton"
            };
            ToolTipService.SetToolTip(RenameButton, Globalization.GetString("Operate_Text_Rename"));
            RenameButton.Click += FolderRename_Click;

            AppBarButton RemovePinButton = new AppBarButton
            {
                Icon = new FontIcon { Glyph = "\uE8D9" },
                Visibility = Visibility.Collapsed,
                Name = "RemovePinButton"
            };
            ToolTipService.SetToolTip(RemovePinButton, Globalization.GetString("Operate_Text_Unpin"));
            RemovePinButton.Click += RemovePin_Click;

            Flyout.PrimaryCommands.Add(CopyButton);
            Flyout.PrimaryCommands.Add(CutButton);
            Flyout.PrimaryCommands.Add(DeleteButton);
            Flyout.PrimaryCommands.Add(RenameButton);
            Flyout.PrimaryCommands.Add(RemovePinButton);
            #endregion

            #region SecondaryCommand -> OpenButton
            AppBarButton OpenButton = new AppBarButton
            {
                Icon = new SymbolIcon { Symbol = Symbol.OpenFile },
                Label = Globalization.GetString("Operate_Text_Open"),
                FontSize = 13,
                Width = 320
            };
            OpenButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Key = VirtualKey.Enter,
                IsEnabled = false
            });
            OpenButton.Click += OpenButton_Click;

            Flyout.SecondaryCommands.Add(OpenButton);
            #endregion

            #region SecondaryCommand -> OpenFolderInNewTabButton
            AppBarButton OpenFolderInNewTabButton = new AppBarButton
            {
                Label = Globalization.GetString("Operate_Text_NewTab"),
                Width = 320,
                FontSize = 13,
                Icon = new FontIcon
                {
                    FontFamily = FontIconFamily,
                    Glyph = "\uF7ED"
                }
            };
            OpenFolderInNewTabButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Modifiers = VirtualKeyModifiers.Control,
                Key = VirtualKey.T,
                IsEnabled = false
            });
            OpenFolderInNewTabButton.Click += OpenFolderInNewTab_Click;

            Flyout.SecondaryCommands.Add(OpenFolderInNewTabButton);
            #endregion

            #region SecondaryCommand -> OpenFolderInNewWindowButton
            AppBarButton OpenFolderInNewWindowButton = new AppBarButton
            {
                Name = "OpenFolderInNewWindowButton",
                Label = Globalization.GetString("Operate_Text_NewWindow"),
                Width = 320,
                FontSize = 13,
                Icon = new FontIcon
                {
                    FontFamily = FontIconFamily,
                    Glyph = "\uE727"
                }
            };
            OpenFolderInNewWindowButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Modifiers = VirtualKeyModifiers.Control,
                Key = VirtualKey.Q,
                IsEnabled = false
            });
            OpenFolderInNewWindowButton.Click += OpenFolderInNewWindow_Click;

            Flyout.SecondaryCommands.Add(OpenFolderInNewWindowButton);
            #endregion

            #region SecondaryCommand -> OpenFolderInVerticalSplitViewButton
            AppBarButton OpenFolderInVerticalSplitViewButton = new AppBarButton
            {
                Label = Globalization.GetString("Operate_Text_SplitView"),
                Width = 320,
                FontSize = 13,
                Name = "OpenFolderInVerticalSplitView",
                Visibility = Visibility.Collapsed,
                Icon = new FontIcon
                {
                    FontFamily = FontIconFamily,
                    Glyph = "\uEA61"
                }
            };
            OpenFolderInVerticalSplitViewButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Modifiers = VirtualKeyModifiers.Control,
                Key = VirtualKey.B,
                IsEnabled = false
            });
            OpenFolderInVerticalSplitViewButton.Click += OpenFolderInVerticalSplitView_Click;

            Flyout.SecondaryCommands.Add(OpenFolderInVerticalSplitViewButton);
            #endregion

            Flyout.SecondaryCommands.Add(new AppBarSeparator());

            #region SecondaryCommand -> SendToButton
            AppBarButton SendToButton = new AppBarButton
            {
                Icon = new SymbolIcon { Symbol = Symbol.Send },
                Label = Globalization.GetString("SendTo/Label"),
                Name = "FolderSendToButton",
                FontSize = 13,
                Width = 320
            };

            MenuFlyout SendToFlyout = new MenuFlyout();
            SendToFlyout.Opening += SendToFlyout_Opening;

            SendToButton.Flyout = SendToFlyout;

            Flyout.SecondaryCommands.Add(SendToButton);
            #endregion

            #region SecondaryCommand -> PropertyButton
            AppBarButton PropertyButton = new AppBarButton
            {
                Icon = new SymbolIcon { Symbol = Symbol.Tag },
                Width = 320,
                FontSize = 13,
                Label = Globalization.GetString("Operate_Text_Property")
            };
            PropertyButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Modifiers = VirtualKeyModifiers.Menu,
                Key = VirtualKey.Enter,
                IsEnabled = false
            });
            PropertyButton.Click += FolderProperty_Click;

            Flyout.SecondaryCommands.Add(PropertyButton);
            #endregion

            return Flyout;
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
            {
                if (Content == TreeViewNodeContent.QuickAccessNode)
                {
                    Node.IsExpanded = !Node.IsExpanded;
                }
                else if (CurrentPresenter != null)
                {
                    if (!await CurrentPresenter.DisplayItemsInFolderAsync(Content.Path))
                    {
                        CommonContentDialog Dialog = new CommonContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };

                        await Dialog.ShowAsync();
                    }
                }
            }
        }

        private void RightTabFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
        {
            if (sender is CommandBarFlyout Flyout)
            {
                foreach (FlyoutBase SubFlyout in Flyout.SecondaryCommands.OfType<AppBarButton>().Select((Btn) => Btn.Flyout).OfType<FlyoutBase>())
                {
                    SubFlyout.Hide();
                }
            }
        }

        private void TList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue)
            {
                args.RegisterUpdateCallback(async (s, e) =>
                {
                    if (e.Item is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
                    {
                        await Content.LoadAsync().ConfigureAwait(false);
                    }
                });
            }
        }

        private void AddressExtensionSubFolderList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue)
            {
                args.RegisterUpdateCallback(async (s, e) =>
                {
                    if (e.Item is FileSystemStorageFolder Item)
                    {
                        try
                        {
                            await Item.LoadAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            LogTracer.Log(ex, $"Could not load the storage item, StorageType: {Item.GetType().FullName}, Path: {Item.Path}");
                        }
                    }
                });
            }
        }

        private async void AddressButtonContainer_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue && args.Item is AddressBlock Block)
            {
                await Block.LoadAsync().ConfigureAwait(false);
            }
        }

        private void AddressNavigationHistoryFlyoutList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue)
            {
                args.RegisterUpdateCallback(async (s, e) =>
                {
                    if (e.Item is NavigationRecordDisplay Record)
                    {
                        await Record.LoadAsync().ConfigureAwait(false);
                    }
                });
            }
        }

        public void RefreshAddressButton(string Path)
        {
            if (!string.IsNullOrEmpty(Path))
            {
                try
                {
                    AddressButtonExecution.Execute(() =>
                    {
                        string RootPath = string.Empty;

                        string[] CurrentSplit = Path.Split(@"\", StringSplitOptions.RemoveEmptyEntries);

                        if (Path.StartsWith(@"\\?\"))
                        {
                            if (CurrentSplit.Length > 0)
                            {
                                RootPath = $@"\\?\{CurrentSplit[1]}";
                                CurrentSplit = CurrentSplit.Skip(2).Prepend(RootPath).ToArray();
                            }
                        }
                        else if (Path.StartsWith(@"\\"))
                        {
                            if (CurrentSplit.Length > 0)
                            {
                                RootPath = $@"\\{CurrentSplit[0]}";
                                CurrentSplit[0] = RootPath;
                            }
                        }
                        else if (Regex.IsMatch(Path, @"^ftps?:\\{1,2}[^\\]+.*", RegexOptions.IgnoreCase))
                        {
                            Path = Regex.Replace(Path, @"\\+", @"\");

                            if (CurrentSplit.Length > 0)
                            {
                                RootPath = string.Join(@"\", CurrentSplit.Take(2));
                                CurrentSplit = CurrentSplit.Skip(2).Prepend(RootPath).ToArray();
                            }
                        }
                        else
                        {
                            RootPath = System.IO.Path.GetPathRoot(Path);
                        }

                        if (AddressButtonList.Count == 0)
                        {
                            if (Path.StartsWith(@"\\?\") || !Path.StartsWith(@"\\"))
                            {
                                AddressButtonList.Add(new AddressBlock(RootVirtualFolder.Current.Path, RootVirtualFolder.Current.DisplayName));
                            }

                            if (!string.IsNullOrEmpty(RootPath))
                            {
                                AddressButtonList.Add(new AddressBlock(RootPath, CommonAccessCollection.DriveList.FirstOrDefault((Drive) => RootPath.Equals(Drive.Path, StringComparison.OrdinalIgnoreCase))?.DisplayName));

                                PathAnalysis Analysis = new PathAnalysis(Path, RootPath);

                                while (Analysis.HasNextLevel)
                                {
                                    AddressButtonList.Add(new AddressBlock(Analysis.NextFullPath()));
                                }
                            }
                        }
                        else if (Path.Equals(RootVirtualFolder.Current.Path, StringComparison.OrdinalIgnoreCase)
                                 && AddressButtonList.First().Path.Equals(RootVirtualFolder.Current.Path, StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (AddressBlock Block in AddressButtonList.Skip(1))
                            {
                                Block.BlockType = AddressBlockType.Gray;
                            }
                        }
                        else
                        {
                            string LastPath = AddressButtonList.LastOrDefault((Block) => Block.BlockType == AddressBlockType.Normal)?.Path ?? string.Empty;
                            string LastGrayPath = AddressButtonList.LastOrDefault((Block) => Block.BlockType == AddressBlockType.Gray)?.Path ?? string.Empty;

                            if (string.IsNullOrEmpty(LastGrayPath) && LastPath.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
                            {
                                string[] LastPathSplit = LastPath.Split(@"\", StringSplitOptions.RemoveEmptyEntries);

                                if (LastPathSplit.Length > 0)
                                {
                                    if (Path.StartsWith(@"\\?\"))
                                    {
                                        if (LastPathSplit.Length > 1)
                                        {
                                            LastPathSplit = LastPathSplit.Skip(2).Prepend($@"\\?\{LastPathSplit[1]}").ToArray();
                                        }
                                    }
                                    else if (LastPathSplit.Length > 0)
                                    {
                                        if (LastPath.StartsWith(@"\\"))
                                        {
                                            LastPathSplit[0] = $@"\\{LastPathSplit[0]}";
                                        }
                                        else if (Regex.IsMatch(LastPath, @"^ftps?:\\{1,2}[^\\]+.*", RegexOptions.IgnoreCase))
                                        {
                                            LastPathSplit = LastPathSplit.Skip(2).Prepend(string.Join(@"\", LastPathSplit.Take(2))).ToArray();
                                        }
                                    }
                                }

                                for (int i = LastPathSplit.Length - CurrentSplit.Length - 1; i >= 0; i--)
                                {
                                    AddressButtonList[AddressButtonList.Count - 1 - i].BlockType = AddressBlockType.Gray;
                                }
                            }
                            else if (Path.StartsWith(LastGrayPath, StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (AddressBlock GrayBlock in AddressButtonList.Where((Block) => Block.BlockType == AddressBlockType.Gray))
                                {
                                    GrayBlock.BlockType = AddressBlockType.Normal;
                                }
                            }
                            else if (LastGrayPath.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
                            {
                                if (LastPath.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
                                {
                                    string[] LastGrayPathSplit = LastGrayPath.Split(@"\", StringSplitOptions.RemoveEmptyEntries);

                                    if (LastGrayPathSplit.Length > 0)
                                    {
                                        if (LastGrayPath.StartsWith(@"\\?\"))
                                        {
                                            if (LastGrayPathSplit.Length > 1)
                                            {
                                                LastGrayPathSplit = LastGrayPathSplit.Skip(2).Prepend($@"\\?\{LastGrayPathSplit[1]}").ToArray();
                                            }
                                        }
                                        else if (LastGrayPathSplit.Length > 0)
                                        {
                                            if (LastGrayPath.StartsWith(@"\\"))
                                            {
                                                LastGrayPathSplit[0] = $@"\\{LastGrayPathSplit[0]}";
                                            }
                                            else if (Regex.IsMatch(LastGrayPath, @"^ftps?:\\{1,2}[^\\]+.*", RegexOptions.IgnoreCase))
                                            {
                                                LastGrayPathSplit = LastGrayPathSplit.Skip(2).Prepend(string.Join(@"\", LastGrayPathSplit.Take(2))).ToArray();
                                            }
                                        }
                                    }

                                    for (int i = LastGrayPathSplit.Length - CurrentSplit.Length - 1; i >= 0; i--)
                                    {
                                        AddressButtonList[AddressButtonList.Count - 1 - i].BlockType = AddressBlockType.Gray;
                                    }
                                }
                                else if (Path.StartsWith(LastPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    string[] LastPathSplit = LastPath.Split(@"\", StringSplitOptions.RemoveEmptyEntries);

                                    if (LastPathSplit.Length > 0)
                                    {
                                        if (LastPath.StartsWith(@"\\?\"))
                                        {
                                            if (LastPathSplit.Length > 1)
                                            {
                                                LastPathSplit = LastPathSplit.Skip(2).Prepend($@"\\?\{LastPathSplit[1]}").ToArray();
                                            }
                                        }
                                        else if (LastPathSplit.Length > 0)
                                        {
                                            if (LastPath.StartsWith(@"\\"))
                                            {
                                                LastPathSplit[0] = $@"\\{LastPathSplit[0]}";
                                            }
                                            else if (Regex.IsMatch(LastPath, @"^ftps?:\\{1,2}[^\\]+.*", RegexOptions.IgnoreCase))
                                            {
                                                LastPathSplit = LastPathSplit.Skip(2).Prepend(string.Join(@"\", LastPathSplit.Take(2))).ToArray();
                                            }
                                        }
                                    }

                                    for (int i = 0; i < CurrentSplit.Length - LastPathSplit.Length; i++)
                                    {
                                        if (AddressButtonList.FirstOrDefault((Block) => Block.BlockType == AddressBlockType.Gray) is AddressBlock GrayBlock)
                                        {
                                            GrayBlock.BlockType = AddressBlockType.Normal;
                                        }
                                    }
                                }
                                else if (LastPath.Equals(RootVirtualFolder.Current.Path, StringComparison.OrdinalIgnoreCase))
                                {
                                    foreach (AddressBlock GrayBlock in AddressButtonList.Skip(1).Take(CurrentSplit.Length))
                                    {
                                        GrayBlock.BlockType = AddressBlockType.Normal;
                                    }
                                }
                            }

                            string CurrentPath = AddressButtonList.LastOrDefault((Block) => Block.BlockType == AddressBlockType.Normal)?.Path ?? string.Empty;
                            string CurrentGrayPath = AddressButtonList.LastOrDefault((Block) => Block.BlockType == AddressBlockType.Gray)?.Path ?? string.Empty;

                            string[] OriginSplit = CurrentPath.Split(@"\", StringSplitOptions.RemoveEmptyEntries);

                            if (OriginSplit.Length > 0)
                            {
                                if (CurrentPath.StartsWith(@"\\?\"))
                                {
                                    if (OriginSplit.Length > 1)
                                    {
                                        OriginSplit = OriginSplit.Skip(2).Prepend($@"\\?\{OriginSplit[1]}").ToArray();
                                    }
                                }
                                else if (OriginSplit.Length > 0)
                                {
                                    if (CurrentPath.StartsWith(@"\\"))
                                    {
                                        OriginSplit[0] = $@"\\{OriginSplit[0]}";
                                    }
                                    else if (Regex.IsMatch(CurrentPath, @"^ftps?:\\{1,2}[^\\]+.*", RegexOptions.IgnoreCase))
                                    {
                                        OriginSplit = OriginSplit.Skip(2).Prepend(string.Join(@"\", OriginSplit.Take(2))).ToArray();
                                    }
                                }
                            }

                            List<string> IntersectList = new List<string>(Math.Min(CurrentSplit.Length, OriginSplit.Length));

                            for (int i = 0; i < Math.Min(CurrentSplit.Length, OriginSplit.Length); i++)
                            {
                                if (!CurrentSplit[i].Equals(OriginSplit[i], StringComparison.OrdinalIgnoreCase))
                                {
                                    break;
                                }

                                IntersectList.Add(CurrentSplit[i]);
                            }

                            if (IntersectList.Count == 0)
                            {
                                AddressButtonList.Clear();

                                if (Path.StartsWith(@"\\?\") || !Path.StartsWith(@"\\"))
                                {
                                    AddressButtonList.Add(new AddressBlock(RootVirtualFolder.Current.Path, RootVirtualFolder.Current.DisplayName));
                                }

                                if (!string.IsNullOrEmpty(RootPath))
                                {
                                    AddressButtonList.Add(new AddressBlock(RootPath, CommonAccessCollection.DriveList.FirstOrDefault((Drive) => RootPath.Equals(Drive.Path, StringComparison.OrdinalIgnoreCase))?.DisplayName));

                                    PathAnalysis Analysis = new PathAnalysis(Path, RootPath);

                                    while (Analysis.HasNextLevel)
                                    {
                                        AddressButtonList.Add(new AddressBlock(Analysis.NextFullPath()));
                                    }
                                }
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(CurrentGrayPath)
                                    || !Path.StartsWith(CurrentPath, StringComparison.OrdinalIgnoreCase)
                                    || !(Path.StartsWith(CurrentGrayPath, StringComparison.OrdinalIgnoreCase) || CurrentGrayPath.StartsWith(Path, StringComparison.OrdinalIgnoreCase)))
                                {
                                    int LimitIndex = IntersectList.Count;

                                    if (AddressButtonList.Any((Block) => Block.Path.Equals(RootVirtualFolder.Current.Path, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        LimitIndex += 1;
                                    }

                                    for (int i = AddressButtonList.Count - 1; i >= LimitIndex; i--)
                                    {
                                        AddressButtonList.RemoveAt(i);
                                    }
                                }

                                if (!Path.Equals(RootVirtualFolder.Current.Path, StringComparison.OrdinalIgnoreCase))
                                {
                                    string BaseString = IntersectList.Count > 1 ? string.Join('\\', CurrentSplit.Take(IntersectList.Count)) : $"{CurrentSplit.First()}\\";

                                    PathAnalysis Analysis = new PathAnalysis(Path, BaseString);

                                    while (Analysis.HasNextLevel)
                                    {
                                        AddressButtonList.Add(new AddressBlock(Analysis.NextFullPath()));
                                    }
                                }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"{nameof(RefreshAddressButton)} throw an exception");
                }
            }
        }

        public async Task<bool> ExecuteGoBackActionIfAvailableAsync()
        {
            if (CurrentPresenter.BackNavigationStack.TryPeek(out NavigationRelatedRecord CurrentRecord))
            {
                //We must check whether the record path is still exists first to avoid change the navigation stack but could not enter the folder later
                if (await FileSystemStorageItemBase.CheckExistsAsync(CurrentRecord.Path))
                {
                    CurrentPresenter.BackNavigationStack.TryPop(out _);

                    if (CurrentPresenter.CurrentFolder is FileSystemStorageFolder CurrentFolder)
                    {
                        IReadOnlyList<string> SelectedItemPathList = CurrentPresenter.SelectedItems.Select((Item) => Item.Path).ToArray();

                        if (SelectedItemPathList.Count > 1)
                        {
                            CurrentPresenter.ForwardNavigationStack.Push(new NavigationRelatedRecord
                            {
                                Path = CurrentFolder.Path,
                                SelectedItemPath = string.Empty
                            });
                        }
                        else
                        {
                            CurrentPresenter.ForwardNavigationStack.Push(new NavigationRelatedRecord
                            {
                                Path = CurrentFolder.Path,
                                SelectedItemPath = SelectedItemPathList.SingleOrDefault() ?? string.Empty
                            });
                        }
                    }

                    if (await CurrentPresenter.DisplayItemsInFolderAsync(CurrentRecord.Path, SkipNavigationRecord: true))
                    {
                        if (!string.IsNullOrEmpty(CurrentRecord.SelectedItemPath) && CurrentPresenter.FileCollection.FirstOrDefault((Item) => Item.Path.Equals(CurrentRecord.SelectedItemPath, StringComparison.OrdinalIgnoreCase)) is FileSystemStorageItemBase Item)
                        {
                            CurrentPresenter.SelectedItem = Item;
                            CurrentPresenter.ItemPresenter.ScrollIntoView(Item, ScrollIntoViewAlignment.Leading);
                        }

                        return true;
                    }
                    else
                    {
                        CommonContentDialog Dialog = new CommonContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{CurrentRecord.Path}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };

                        await Dialog.ShowAsync();
                    }
                }
                else
                {
                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{CurrentRecord.Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    await Dialog.ShowAsync();
                }
            }

            return false;
        }

        public async Task<bool> ExecuteGoForwardActionIfAvailableAsync()
        {
            if (CurrentPresenter.ForwardNavigationStack.TryPeek(out NavigationRelatedRecord CurrentRecord))
            {
                //We must check whether the record path is still exists first to avoid change the navigation stack but could not enter the folder later
                if (await FileSystemStorageItemBase.CheckExistsAsync(CurrentRecord.Path))
                {
                    CurrentPresenter.ForwardNavigationStack.TryPop(out _);

                    if (CurrentPresenter.CurrentFolder is FileSystemStorageFolder CurrentFolder)
                    {
                        IReadOnlyList<string> SelectedItemPathList = CurrentPresenter.SelectedItems.Select((Item) => Item.Path).ToArray();

                        if (SelectedItemPathList.Count > 1)
                        {
                            CurrentPresenter.BackNavigationStack.Push(new NavigationRelatedRecord
                            {
                                Path = CurrentFolder.Path,
                                SelectedItemPath = string.Empty
                            });
                        }
                        else
                        {
                            CurrentPresenter.BackNavigationStack.Push(new NavigationRelatedRecord
                            {
                                Path = CurrentFolder.Path,
                                SelectedItemPath = SelectedItemPathList.SingleOrDefault() ?? string.Empty
                            });
                        }
                    }

                    if (await CurrentPresenter.DisplayItemsInFolderAsync(CurrentRecord.Path, SkipNavigationRecord: true))
                    {
                        if (!string.IsNullOrEmpty(CurrentRecord.SelectedItemPath) && CurrentPresenter.FileCollection.FirstOrDefault((Item) => Item.Path.Equals(CurrentRecord.SelectedItemPath, StringComparison.OrdinalIgnoreCase)) is FileSystemStorageItemBase Item)
                        {
                            CurrentPresenter.SelectedItem = Item;
                            CurrentPresenter.ItemPresenter.ScrollIntoView(Item, ScrollIntoViewAlignment.Leading);
                        }

                        return true;
                    }
                    else
                    {
                        CommonContentDialog Dialog = new CommonContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{CurrentRecord.Path}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };

                        await Dialog.ShowAsync();
                    }
                }
                else
                {
                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{CurrentRecord.Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    await Dialog.ShowAsync();
                }
            }

            return false;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            try
            {
                if (e.NavigationMode == NavigationMode.New
                    && e.Parameter is TabItemContentRenderer Renderer)
                {
                    GoBackRecord.AddHandler(PointerPressedEvent, GoBackButtonPressedHandler, true);
                    GoBackRecord.AddHandler(PointerReleasedEvent, GoBackButtonReleasedHandler, true);
                    GoForwardRecord.AddHandler(PointerPressedEvent, GoForwardButtonPressedHandler, true);
                    GoForwardRecord.AddHandler(PointerReleasedEvent, GoForwardButtonReleasedHandler, true);

                    this.Renderer = Renderer;

                    await InitializeAsync(Renderer.InitializePaths.Where((Path) => !string.IsNullOrWhiteSpace(Path)).ToArray());
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "An unexpected exception was threw when navigating to FileControl");
            }
        }

        private async void CommonAccessCollection_LibraryChanged(object sender, LibraryChangedDeferredEventArgs args)
        {
            EventDeferral Deferral = args.GetDeferral();

            try
            {
                switch (args.Type)
                {
                    case CommonChangeType.Added:
                        {
                            if (FolderTree.RootNodes.FirstOrDefault((Node) => Node.Content == TreeViewNodeContent.QuickAccessNode) is TreeViewNode QuickAccessNode)
                            {
                                if (QuickAccessNode.IsExpanded)
                                {
                                    TreeViewNodeContent Content = await TreeViewNodeContent.CreateAsync(args.StorageItem);

                                    QuickAccessNode.Children.Add(new TreeViewNode
                                    {
                                        IsExpanded = false,
                                        Content = Content,
                                        HasUnrealizedChildren = Content.HasChildren
                                    });
                                }
                            }
                            else
                            {
                                FolderTree.RootNodes.Add(new TreeViewNode
                                {
                                    Content = TreeViewNodeContent.QuickAccessNode,
                                    IsExpanded = false,
                                    HasUnrealizedChildren = true
                                });
                            }

                            break;
                        }
                    case CommonChangeType.Removed:
                        {
                            if (FolderTree.RootNodes.FirstOrDefault((Node) => Node.Content == TreeViewNodeContent.QuickAccessNode) is TreeViewNode QuickAccessNode)
                            {
                                if (QuickAccessNode.IsExpanded)
                                {
                                    QuickAccessNode.Children.Remove(QuickAccessNode.Children.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path.Equals(args.StorageItem.Path, StringComparison.OrdinalIgnoreCase)));
                                }
                            }

                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw in {nameof(CommonAccessCollection_LibraryChanged)}");
            }
            finally
            {
                Deferral.Complete();
            }
        }

        private async void CommonAccessCollection_DriveChanged(object sender, DriveChangedDeferredEventArgs args)
        {
            EventDeferral Deferral = args.GetDeferral();

            try
            {
                switch (args.Type)
                {
                    case CommonChangeType.Added when !string.IsNullOrWhiteSpace(args.StorageItem.Path):
                        {
                            if (FolderTree.RootNodes.Select((Node) => Node.Content).OfType<TreeViewNodeContent>().All((Content) => !Content.Path.Equals(args.StorageItem.Path, StringComparison.OrdinalIgnoreCase)))
                            {
                                TreeViewNodeContent Content = await TreeViewNodeContent.CreateAsync(args.StorageItem);

                                FolderTree.RootNodes.Add(new TreeViewNode
                                {
                                    IsExpanded = false,
                                    Content = Content,
                                    HasUnrealizedChildren = Content.HasChildren
                                });
                            }

                            if (FolderTree.RootNodes.FirstOrDefault() is TreeViewNode Node)
                            {
                                await FolderTree.SelectNodeAndScrollToVerticalAsync(Node);
                            }

                            break;
                        }
                    case CommonChangeType.Removed:
                        {
                            if (FolderTree.RootNodes.FirstOrDefault((Node) => ((Node.Content as TreeViewNodeContent)?.Path.Equals(args.StorageItem.Path, StringComparison.OrdinalIgnoreCase)).GetValueOrDefault()) is TreeViewNode Node)
                            {
                                FolderTree.RootNodes.Remove(Node);
                            }

                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw in {nameof(CommonAccessCollection_DriveChanged)}");
            }
            finally
            {
                Deferral.Complete();
            }
        }

        /// <summary>
        /// 执行文件目录的初始化
        /// </summary>
        private async Task InitializeAsync(IReadOnlyList<string> InitPathArray)
        {
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    if (FolderTree.IsLoaded)
                    {
                        if (FolderTree.FindChildOfType<TreeViewList>() is TreeViewList InnerList)
                        {
                            InnerList.ContainerContentChanging += TList_ContainerContentChanging;
                            ScrollViewer.SetVerticalScrollBarVisibility(InnerList, ScrollBarVisibility.Hidden);
                            TreeViewColumnWidthSaver.Current.TreeViewVisibility = SettingPage.IsDetachTreeViewAndPresenter ? Visibility.Collapsed : Visibility.Visible;
                            break;
                        }
                    }

                    await Task.Delay(300);
                }

                if (FolderTree.RootNodes.All((Node) => Node.Content != TreeViewNodeContent.QuickAccessNode))
                {
                    FolderTree.RootNodes.Add(new TreeViewNode
                    {
                        Content = TreeViewNodeContent.QuickAccessNode,
                        IsExpanded = false,
                        HasUnrealizedChildren = true
                    });
                }

                IReadOnlyList<Task<TreeViewNode>> SyncTreeViewFromDriveList(IEnumerable<FileSystemStorageFolder> DriveList)
                {
                    List<Task<TreeViewNode>> LongLoadList = new List<Task<TreeViewNode>>();

                    foreach (FileSystemStorageFolder DriveFolder in DriveList)
                    {
                        if (FolderTree.RootNodes.Select((Node) => (Node.Content as TreeViewNodeContent)?.Path).All((Path) => !Path.Equals(DriveFolder.Path, StringComparison.OrdinalIgnoreCase)))
                        {
                            LongLoadList.Add(TreeViewNodeContent.CreateAsync(DriveFolder)
                                                                .ContinueWith((PreviousTask) =>
                                                                {
                                                                    if (PreviousTask.Exception is Exception Ex)
                                                                    {
                                                                        throw Ex;
                                                                    }
                                                                    else
                                                                    {
                                                                        return new TreeViewNode
                                                                        {
                                                                            Content = PreviousTask.Result,
                                                                            IsExpanded = false,
                                                                            HasUnrealizedChildren = PreviousTask.Result.HasChildren
                                                                        };
                                                                    }
                                                                }, TaskScheduler.FromCurrentSynchronizationContext()));
                        }
                    }

                    return LongLoadList;
                }

                IEnumerable<FileSystemStorageFolder> CurrentDrives = CommonAccessCollection.DriveList.Select((Drive) => Drive.DriveFolder).ToArray();

                IReadOnlyList<Task<TreeViewNode>> TaskList = SyncTreeViewFromDriveList(CurrentDrives);

                if (InitPathArray.Count == 1
                    && await FileSystemStorageItemBase.OpenAsync(InitPathArray[0]) is FileSystemStorageFile File)
                {
                    if (Helper.GetSuitableInnerViewerPageType(File, out Type PageType))
                    {
                        Renderer.RendererFrame.Navigate(PageType, File, AnimationController.Current.IsEnableAnimation ? new DrillInNavigationTransitionInfo() : new SuppressNavigationTransitionInfo());
                    }

                    await CreateNewBladeAsync(File);
                }
                else
                {
                    await foreach (FileSystemStorageItemBase Item in FileSystemStorageItemBase.OpenInBatchAsync(InitPathArray))
                    {
                        await CreateNewBladeAsync(Item);
                    }
                }

                CommonAccessCollection.DriveChanged += CommonAccessCollection_DriveChanged;
                CommonAccessCollection.LibraryChanged += CommonAccessCollection_LibraryChanged;

                foreach (TreeViewNode Node in await Task.WhenAll(TaskList.Concat(SyncTreeViewFromDriveList(CommonAccessCollection.GetMissedDriveBeforeSubscribeEvents()))))
                {
                    if (Node?.Content is TreeViewNodeContent NewContent)
                    {
                        if (FolderTree.RootNodes.Select((Node) => Node.Content)
                                                .OfType<TreeViewNodeContent>()
                                                .Select((Item) => Item.Path)
                                                .All((Path) => !Path.Equals(NewContent.Path, StringComparison.OrdinalIgnoreCase)))
                        {
                            FolderTree.RootNodes.Add(Node);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"Could not initialize the {nameof(FileControl)}");
            }
        }

        /// <summary>
        /// 向特定TreeViewNode节点下添加子节点
        /// </summary>
        /// <param name="Node">节点</param>
        /// <returns></returns>
        private async Task FillTreeNodeAsync(TreeViewNode Node)
        {
            if (Node == null)
            {
                throw new ArgumentNullException(nameof(Node), "Parameter could not be null");
            }

            if (Node.Content is TreeViewNodeContent Content)
            {
                if (await FileSystemStorageItemBase.OpenAsync(Content.Path) is FileSystemStorageFolder Folder)
                {
                    await Dispatcher.RunAndWaitAsyncTask(CoreDispatcherPriority.Low, async () =>
                    {
                        await foreach (FileSystemStorageFolder StorageItem in Folder.GetChildItemsAsync(SettingPage.IsDisplayHiddenItemsEnabled, SettingPage.IsDisplayProtectedSystemItemsEnabled, Filter: BasicFilters.Folder).Cast<FileSystemStorageFolder>())
                        {
                            if (!Node.IsExpanded || !Node.CanTraceToRootNode(FolderTree.RootNodes.ToArray()))
                            {
                                break;
                            }

                            TreeViewNodeContent Content = await TreeViewNodeContent.CreateAsync(StorageItem);

                            Node.Children.Add(new TreeViewNode
                            {
                                Content = Content,
                                HasUnrealizedChildren = Content.HasChildren
                            });
                        }
                    });
                }
            }
        }

        private async void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            try
            {
                if (args.Node.Content == TreeViewNodeContent.QuickAccessNode)
                {
                    if (!SettingPage.IsLibraryExpanderExpanded)
                    {
                        await CommonAccessCollection.LoadLibraryFoldersAsync();
                    }

                    IEnumerable<FileSystemStorageFolder> QuickAccessNodeChildren = CommonAccessCollection.LibraryList;

                    if (SettingPage.IsDisplayLabelFolderInQuickAccessNode)
                    {
                        QuickAccessNodeChildren = Enum.GetValues(typeof(LabelKind)).Cast<LabelKind>()
                                                                                   .Where((Kind) => Kind != LabelKind.None)
                                                                                   .Select((Kind) => LabelCollectionVirtualFolder.GetFolderFromLabel(Kind))
                                                                                   .Concat(QuickAccessNodeChildren);
                    }

                    foreach (FileSystemStorageFolder Folder in QuickAccessNodeChildren)
                    {
                        if (!args.Node.IsExpanded)
                        {
                            break;
                        }

                        TreeViewNodeContent Content = await TreeViewNodeContent.CreateAsync(Folder);

                        args.Node.Children.Add(new TreeViewNode
                        {
                            Content = Content,
                            IsExpanded = false,
                            HasUnrealizedChildren = Content.HasChildren
                        });
                    }
                }
                else
                {
                    await FillTreeNodeAsync(args.Node);
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw in {nameof(FolderTree_Expanding)}");
            }
            finally
            {
                if (!args.Node.IsExpanded)
                {
                    args.Node.Children.Clear();
                }
            }
        }

        private async void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            try
            {
                if (args.InvokedItem is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
                {
                    if (Content == TreeViewNodeContent.QuickAccessNode)
                    {
                        Node.IsExpanded = !Node.IsExpanded;
                    }
                    else if (CurrentPresenter is FilePresenter Presenter)
                    {
                        if (!await Presenter.DisplayItemsInFolderAsync(Content.Path))
                        {
                            CommonContentDialog Dialog = new CommonContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            await Dialog.ShowAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not enter to the node of folder tree");
            }
        }

        private async void FolderDelete_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuFlyout.Hide();

            if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent TargetContent)
            {
                if (await FileSystemStorageItemBase.CheckExistsAsync(TargetContent.Path))
                {
                    bool ExecuteDelete = false;
                    bool PermanentDelete = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down) | SettingPage.IsAvoidRecycleBinEnabled;

                    if (SettingPage.IsDoubleConfirmOnDeletionEnabled)
                    {
                        CommonContentDialog Dialog = new CommonContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_WarningTitle"),
                            PrimaryButtonText = Globalization.GetString("Common_Dialog_ContinueButton"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton"),
                            Content = PermanentDelete ? Globalization.GetString("QueueDialog_DeleteFolderPermanent_Content") : Globalization.GetString("QueueDialog_DeleteFolder_Content")
                        };

                        if (await Dialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            ExecuteDelete = true;
                        }
                    }
                    else
                    {
                        ExecuteDelete = true;
                    }

                    if (ExecuteDelete)
                    {
                        OperationListDeleteModel Model = new OperationListDeleteModel(new string[] { TargetContent.Path }, PermanentDelete);

                        QueueTaskController.RegisterPostAction(Model, async (s, e) =>
                        {
                            EventDeferral Deferral = e.GetDeferral();

                            try
                            {
                                if (e.Status == OperationStatus.Completed)
                                {
                                    await Dispatcher.RunAndWaitAsyncTask(CoreDispatcherPriority.Low, async () =>
                                    {
                                        foreach (FilePresenter Presenter in TabViewContainer.Current.TabCollection.Select((Tab) => Tab.Content)
                                                                                                                  .Cast<Frame>()
                                                                                                                  .Select((Frame) => Frame.Content)
                                                                                                                  .Cast<TabItemContentRenderer>()
                                                                                                                  .SelectMany((Renderer) => Renderer.Presenters))
                                        {
                                            FileSystemStorageFolder CurrentFolder = Presenter.CurrentFolder;

                                            if (CurrentFolder is INotWin32StorageFolder && Path.GetDirectoryName(TargetContent.Path).Equals(CurrentFolder.Path, StringComparison.OrdinalIgnoreCase))
                                            {
                                                await Presenter.AreaWatcher.InvokeRemovedEventManuallyAsync(new FileRemovedDeferredEventArgs(TargetContent.Path));
                                            }
                                        }

                                        foreach (TreeViewNode RootNode in FolderTree.RootNodes)
                                        {
                                            await RootNode.UpdateSubNodeAsync();
                                        }
                                    });

                                    SQLite.Current.DeleteLabelKindByPath(TargetContent.Path);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Log(ex, $"Failed to execute the post action delegate of {nameof(FolderDelete_Click)}");
                            }
                            finally
                            {
                                Deferral.Complete();
                            }
                        });

                        QueueTaskController.EnqueueDeleteOpeartion(Model);
                    }
                }
                else
                {
                    await new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    }.ShowAsync();
                }
            }
        }

        private void FolderTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            args.Node.Children.Clear();
        }

        private async void FolderRename_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuFlyout.Hide();

            if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
            {
                if (await FileSystemStorageItemBase.OpenAsync(Content.Path) is FileSystemStorageFolder Folder)
                {
                    RenameDialog dialog = new RenameDialog(Folder);

                    if ((await dialog.ShowAsync()) == ContentDialogResult.Primary)
                    {
                        string OriginName = Folder.Name;
                        string NewName = dialog.DesireNameMap[OriginName];

                        if (OriginName != NewName)
                        {
                            if (!OriginName.Equals(NewName, StringComparison.OrdinalIgnoreCase)
                                && await FileSystemStorageItemBase.CheckExistsAsync(Path.Combine(Folder.Path, NewName)))
                            {
                                CommonContentDialog Dialog = new CommonContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_RenameExist_Content"),
                                    PrimaryButtonText = Globalization.GetString("Common_Dialog_ContinueButton"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                };

                                if (await Dialog.ShowAsync() != ContentDialogResult.Primary)
                                {
                                    return;
                                }
                            }

                            OperationListRenameModel Model = new OperationListRenameModel(Folder.Path, Path.Combine(Path.GetDirectoryName(Folder.Path), NewName));

                            QueueTaskController.RegisterPostAction(Model, async (s, e) =>
                            {
                                EventDeferral Deferral = e.GetDeferral();

                                try
                                {
                                    await Dispatcher.RunAndWaitAsyncTask(CoreDispatcherPriority.Low, async () =>
                                    {
                                        if (e.Status == OperationStatus.Completed && !SettingPage.IsDetachTreeViewAndPresenter)
                                        {
                                            string ParentFolder = Path.GetDirectoryName(Folder.Path);

                                            if (FolderTree.RootNodes.FirstOrDefault((Node) => Node.Content == TreeViewNodeContent.QuickAccessNode) is TreeViewNode QuickAccessNode)
                                            {
                                                foreach (TreeViewNode Node in QuickAccessNode.Children.Where((Node) => Node.Content is TreeViewNodeContent Content && ParentFolder.StartsWith(Content.Path, StringComparison.OrdinalIgnoreCase)))
                                                {
                                                    await Node.UpdateSubNodeAsync();
                                                }
                                            }

                                            if (FolderTree.RootNodes.FirstOrDefault((Node) => Node.Content is TreeViewNodeContent Content && Path.GetPathRoot(ParentFolder).Equals(Content.Path, StringComparison.OrdinalIgnoreCase)) is TreeViewNode RootNode)
                                            {
                                                if (await RootNode.GetTargetNodeAsync(new PathAnalysis(ParentFolder)) is TreeViewNode CurrentNode)
                                                {
                                                    await CurrentNode.UpdateSubNodeAsync();
                                                }
                                            }
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    LogTracer.Log(ex, $"Failed to execute the post action delegate of {nameof(FolderRename_Click)}");
                                }
                                finally
                                {
                                    Deferral.Complete();
                                }
                            });

                            QueueTaskController.EnqueueRenameOpeartion(Model);
                        }
                    }
                }
                else
                {
                    CommonContentDialog ErrorDialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    await ErrorDialog.ShowAsync();
                }
            }
        }


        private async void FolderProperty_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuFlyout.Hide();

            if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
            {
                if (await FileSystemStorageItemBase.OpenAsync(Content.Path) is FileSystemStorageItemBase Item)
                {
                    if (FolderTree.RootNodes.Any((Node) => (Node.Content as TreeViewNodeContent).Path.Equals(Item.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (CommonAccessCollection.DriveList.FirstOrDefault((Device) => Device.Path.Equals(Item.Path, StringComparison.OrdinalIgnoreCase)) is DriveDataBase Drive)
                        {
                            PropertiesWindowBase NewWindow = await PropertiesWindowBase.CreateAsync(Drive);
                            await NewWindow.ShowAsync(new Point(Window.Current.Bounds.Width / 2 - 200, Window.Current.Bounds.Height / 2 - 300));
                        }
                        else
                        {
                            PropertiesWindowBase NewWindow = await PropertiesWindowBase.CreateAsync(Item);
                            await NewWindow.ShowAsync(new Point(Window.Current.Bounds.Width / 2 - 200, Window.Current.Bounds.Height / 2 - 300));
                        }
                    }
                    else
                    {
                        PropertiesWindowBase NewWindow = await PropertiesWindowBase.CreateAsync(Item);
                        await NewWindow.ShowAsync(new Point(Window.Current.Bounds.Width / 2 - 200, Window.Current.Bounds.Height / 2 - 300));
                    }
                }
                else
                {
                    await new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    }.ShowAsync();
                }
            }
        }

        private async void GlobeSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                if (args.ChosenSuggestion is SearchSuggestionItem SuggestItem)
                {
                    sender.Text = SuggestItem.Text;
                }
                else
                {
                    sender.Text = args.QueryText;
                }

                if (!string.IsNullOrWhiteSpace(sender.Text))
                {
                    SearchInEverythingEngine.IsEnabled = false;

                    if (CurrentPresenter.CurrentFolder is not INotWin32StorageFolder && await MSStoreHelper.CheckPurchaseStatusAsync())
                    {
                        using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync(Priority: PriorityLevel.High))
                        {
                            SearchInEverythingEngine.IsEnabled = await Exclusive.Controller.CheckIfEverythingIsAvailableAsync();
                        }
                    }

                    switch (SettingPage.SearchEngineMode)
                    {
                        case SearchEngineFlyoutMode.AlwaysPopup:
                            {
                                FlyoutBase.ShowAttachedFlyout(sender);
                                break;
                            }
                        case SearchEngineFlyoutMode.UseBuildInEngineAsDefault:
                            {
                                SearchOptions Options = SearchOptions.LoadSavedConfiguration(SearchCategory.BuiltInEngine);
                                Options.SearchText = sender.Text;
                                Options.SearchFolder = CurrentPresenter.CurrentFolder;
                                Options.DeepSearch |= CurrentPresenter.CurrentFolder is RootVirtualFolder;

                                Frame.Navigate(typeof(SearchPage), Options, AnimationController.Current.IsEnableAnimation ? new DrillInNavigationTransitionInfo() : new SuppressNavigationTransitionInfo());
                                break;
                            }
                        case SearchEngineFlyoutMode.UseEverythingEngineAsDefault:
                            {
                                SearchOptions Options = SearchOptions.LoadSavedConfiguration(SearchCategory.EverythingEngine);
                                Options.SearchText = sender.Text;
                                Options.SearchFolder = CurrentPresenter.CurrentFolder;
                                Options.DeepSearch |= CurrentPresenter.CurrentFolder is RootVirtualFolder;

                                Frame.Navigate(typeof(SearchPage), Options, AnimationController.Current.IsEnableAnimation ? new DrillInNavigationTransitionInfo() : new SuppressNavigationTransitionInfo());
                                break;
                            }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not search the content on query submitted");
            }
        }

        private void GlobeSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                SearchTextExecution.Execute(() =>
                {
                    try
                    {
                        if (args.CheckCurrent())
                        {
                            SearchSuggestionList.Clear();

                            foreach (string Text in SQLite.Current.GetRelatedSearchHistory(sender.Text))
                            {
                                SearchSuggestionList.Add(new SearchSuggestionItem(Text));
                            }
                        }
                    }
                    catch (Exception)
                    {
                        //No need to handle this exception
                    }
                });
            }
        }

        private void GlobeSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            ShouldNotAcceptShortcutKeyInput = true;

            GlobeSearch.FindChildOfType<TextBox>()?.SelectAll();

            SearchSuggestionList.Clear();

            foreach (string Text in SQLite.Current.GetRelatedSearchHistory(GlobeSearch.Text))
            {
                SearchSuggestionList.Add(new SearchSuggestionItem(Text));
            }
        }

        private void GlobeSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            ShouldNotAcceptShortcutKeyInput = false;
        }

        private async void AddressBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            AddressButtonContainer.Visibility = Visibility.Visible;

            if (!string.IsNullOrEmpty(CurrentPresenter?.CurrentFolder?.Path))
            {
                string QueryText = (args.ChosenSuggestion is AddressSuggestionItem SuggestItem ? SuggestItem.Path : args.QueryText).Replace('/', '\\').TrimEnd('\\').Trim();

                if (!string.IsNullOrWhiteSpace(QueryText))
                {
                    try
                    {
                        string StartupLocation = CurrentPresenter.CurrentFolder switch
                        {
                            RootVirtualFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            FileSystemStorageFolder Folder => Folder.Path,
                            _ => null
                        };

                        if (string.Equals(QueryText, "Powershell", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Powershell.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
                            {
                                if (string.IsNullOrEmpty(StartupLocation))
                                {
                                    if (!await Exclusive.Controller.RunAsync("powershell.exe", RunAsAdmin: true, Parameters: "-NoExit"))
                                    {
                                        CommonContentDialog Dialog = new CommonContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        await Dialog.ShowAsync();
                                    }
                                }
                                else
                                {
                                    if (!await Exclusive.Controller.RunAsync("powershell.exe", RunAsAdmin: true, Parameters: new string[] { "-NoExit", "-Command", "Set-Location", StartupLocation }))
                                    {
                                        CommonContentDialog Dialog = new CommonContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        await Dialog.ShowAsync();
                                    }
                                }
                            }
                        }
                        else if (string.Equals(QueryText, "Cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Cmd.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
                            {
                                if (string.IsNullOrEmpty(StartupLocation))
                                {
                                    if (!await Exclusive.Controller.RunAsync("cmd.exe", RunAsAdmin: true))
                                    {
                                        CommonContentDialog Dialog = new CommonContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        await Dialog.ShowAsync();
                                    }
                                }
                                else
                                {
                                    if (!await Exclusive.Controller.RunAsync("cmd.exe", RunAsAdmin: true, Parameters: new string[] { "/k", "cd", "/d", StartupLocation }))
                                    {
                                        CommonContentDialog Dialog = new CommonContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        await Dialog.ShowAsync();
                                    }
                                }
                            }
                        }
                        else if (string.Equals(QueryText, "Wt", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Wt.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
                            {
                                switch (await Launcher.QueryUriSupportAsync(new Uri("ms-windows-store:"), LaunchQuerySupportType.Uri, "Microsoft.WindowsTerminal_8wekyb3d8bbwe"))
                                {
                                    case LaunchQuerySupportStatus.Available:
                                    case LaunchQuerySupportStatus.NotSupported:
                                        {
                                            if (string.IsNullOrEmpty(StartupLocation))
                                            {
                                                if (!await Exclusive.Controller.RunAsync("wt.exe", RunAsAdmin: true))
                                                {
                                                    CommonContentDialog Dialog = new CommonContentDialog
                                                    {
                                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                        Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                    };

                                                    await Dialog.ShowAsync();
                                                }
                                            }
                                            else
                                            {
                                                if (!await Exclusive.Controller.RunAsync("wt.exe", RunAsAdmin: true, Parameters: new string[] { "/d", StartupLocation }))
                                                {
                                                    CommonContentDialog Dialog = new CommonContentDialog
                                                    {
                                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                        Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                    };

                                                    await Dialog.ShowAsync();
                                                }
                                            }

                                            break;
                                        }
                                }
                            }
                        }
                        else
                        {
                            string TargetPath = await EnvironmentVariables.ReplaceVariableWithActualPathAsync(QueryText);

                            using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
                            {
                                TargetPath = await Exclusive.Controller.ConvertShortPathToLongPathAsync(TargetPath);
                            }

                            if (!Regex.IsMatch(TargetPath, @"^((ftps?:\\{1,2}[^\\]+.*)|(\\\\\?\\[^\\]+.*))", RegexOptions.IgnoreCase))
                            {
                                IReadOnlyList<VariableDataPackage> VariablePathList = await EnvironmentVariables.GetVariablePathListAsync();

                                foreach (string GuessPath in VariablePathList.Select((Var) => Path.Combine(Var.Path, QueryText)))
                                {
                                    if (await FileSystemStorageItemBase.CheckExistsAsync(GuessPath))
                                    {
                                        TargetPath = GuessPath;
                                        break;
                                    }
                                }
                            }

                            if (!TargetPath.Equals(CurrentPresenter.CurrentFolder.Path, StringComparison.OrdinalIgnoreCase))
                            {
                                if (CommonAccessCollection.DriveList.FirstOrDefault((Drive) => Drive.Path.TrimEnd('\\').Equals(Path.GetPathRoot(TargetPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) is LockedDriveData LockedDrive)
                                {
                                Retry:
                                    try
                                    {
                                        BitlockerPasswordDialog Dialog = new BitlockerPasswordDialog();

                                        if (await Dialog.ShowAsync() == ContentDialogResult.Primary)
                                        {
                                            if (!await LockedDrive.UnlockAsync(Dialog.Password))
                                            {
                                                throw new UnlockDriveFailedException();
                                            }

                                            if (await DriveDataBase.CreateAsync(LockedDrive) is DriveDataBase RefreshedDrive)
                                            {
                                                if (RefreshedDrive is LockedDriveData)
                                                {
                                                    throw new UnlockDriveFailedException();
                                                }
                                                else
                                                {
                                                    int Index = CommonAccessCollection.DriveList.IndexOf(LockedDrive);

                                                    if (Index >= 0)
                                                    {
                                                        CommonAccessCollection.DriveList.Remove(LockedDrive);
                                                        CommonAccessCollection.DriveList.Insert(Index, RefreshedDrive);
                                                    }
                                                    else
                                                    {
                                                        CommonAccessCollection.DriveList.Add(RefreshedDrive);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                throw new UnauthorizedAccessException(LockedDrive.Path);
                                            }
                                        }
                                        else
                                        {
                                            return;
                                        }
                                    }
                                    catch (UnlockDriveFailedException)
                                    {
                                        CommonContentDialog UnlockFailedDialog = new CommonContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_UnlockBitlockerFailed_Content"),
                                            PrimaryButtonText = Globalization.GetString("Common_Dialog_RetryButton"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                        };

                                        if (await UnlockFailedDialog.ShowAsync() == ContentDialogResult.Primary)
                                        {
                                            goto Retry;
                                        }
                                        else
                                        {
                                            return;
                                        }
                                    }
                                }

                                switch (await FileSystemStorageItemBase.OpenAsync(TargetPath))
                                {
                                    case FileSystemStorageFile File:
                                        {
                                            await CurrentPresenter.OpenSelectedItemAsync(File);

                                            if (SettingPage.IsPathHistoryEnabled)
                                            {
                                                SQLite.Current.SetPathHistory(File.Path);
                                            }

                                            break;
                                        }
                                    case FileSystemStorageFolder Folder:
                                        {
                                            string TargetRootPath = Path.GetPathRoot(Folder.Path);
                                            string CurrentRootPath = Path.GetPathRoot(CurrentPresenter.CurrentFolder.Path);

                                            if (!CurrentRootPath.Equals(TargetRootPath, StringComparison.OrdinalIgnoreCase))
                                            {
                                                if (FolderTree.RootNodes.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path.Equals(TargetRootPath, StringComparison.OrdinalIgnoreCase)) is TreeViewNode TargetRootNode)
                                                {
                                                    await FolderTree.SelectNodeAndScrollToVerticalAsync(TargetRootNode);
                                                    TargetRootNode.IsExpanded = true;
                                                }
                                            }

                                            if (SettingPage.IsPathHistoryEnabled)
                                            {
                                                SQLite.Current.SetPathHistory(Folder.Path);
                                            }

                                            await JumpListController.AddItemAsync(JumpListGroup.Recent, Folder.Path);

                                            if (!await CurrentPresenter.DisplayItemsInFolderAsync(Folder))
                                            {
                                                CommonContentDialog Dialog = new CommonContentDialog
                                                {
                                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                                                };

                                                await Dialog.ShowAsync();
                                            }

                                            break;
                                        }
                                    default:
                                        {
                                            throw new FileNotFoundException(TargetPath);
                                        }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        CommonContentDialog Dialog = new CommonContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{QueryText}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };

                        await Dialog.ShowAsync();
                    }
                }
            }
        }

        private void OnAddressButtonContainerVisibiliyChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (sender is ListView View)
            {
                ShouldNotAcceptShortcutKeyInput = View.Visibility is Visibility.Collapsed;

                if (View.Visibility == Visibility.Visible)
                {
                    AddressBox.Text = string.Empty;
                    FocusBerth.Focus(FocusState.Programmatic);
                }
                else
                {
                    if (string.IsNullOrEmpty(AddressBox.Text))
                    {
                        switch (CurrentPresenter?.CurrentFolder)
                        {
                            case RootVirtualFolder:
                                {
                                    AddressBox.Text = string.Empty;
                                    break;
                                }
                            case FileSystemStorageFolder Folder:
                                {
                                    AddressBox.Text = Folder.Path;
                                    break;
                                }
                            default:
                                {
                                    AddressBox.Text = string.Empty;
                                    break;
                                }
                        }
                    }

                    if (AddressBox.FindChildOfType<TextBox>() is TextBox InnerBox)
                    {
                        InnerBox.SelectAll();
                        InnerBox.SelectionFlyout.ShowMode = FlyoutShowMode.Transient;
                    }

                    AddressSuggestionList.Clear();

                    foreach (string Path in SQLite.Current.GetRelatedPathHistory())
                    {
                        AddressSuggestionList.Add(new AddressSuggestionItem(Path));
                    }
                }
            }
        }

        private async void AddressBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                await AddressTextExecution.ExecuteAsync(async () =>
                {
                    AddressSuggestionList.Clear();

                    try
                    {
                        string InputPath = sender.Text.Replace('/', '\\').Trim();

                        if (string.IsNullOrWhiteSpace(InputPath))
                        {
                            AddressSuggestionList.AddRange(SQLite.Current.GetRelatedPathHistory().Select((Path) => new AddressSuggestionItem(Path)));
                        }
                        else
                        {
                            if (InputPath.IndexOf('%') == 0 && InputPath.LastIndexOf('%') == 0)
                            {
                                IEnumerable<VariableDataPackage> VarSuggestionList = await EnvironmentVariables.GetVariablePathListAsync(InputPath);

                                if (args.CheckCurrent() && VarSuggestionList.Any())
                                {
                                    AddressSuggestionList.AddRange(VarSuggestionList.Select((Pack) => new AddressSuggestionItem(Pack.Path, Pack.Variable, Visibility.Collapsed)));
                                }
                            }
                            else if (!Regex.IsMatch(InputPath, @"^((ftps?:\\{1,2}[^\\]+.*)|(\\\\\?\\[^\\]+.*))", RegexOptions.IgnoreCase))
                            {
                                string TargetPath = await EnvironmentVariables.ReplaceVariableWithActualPathAsync(InputPath);

                                using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
                                {
                                    TargetPath = await Exclusive.Controller.ConvertShortPathToLongPathAsync(TargetPath);
                                }

                                string DirectoryPath = Path.GetDirectoryName(TargetPath);

                                if (await FileSystemStorageItemBase.OpenAsync(string.IsNullOrEmpty(DirectoryPath) ? TargetPath : DirectoryPath) is FileSystemStorageFolder ParentFolder)
                                {
                                    if (args.CheckCurrent())
                                    {
                                        string FileName = Path.GetFileName(TargetPath);

                                        IAsyncEnumerable<FileSystemStorageItemBase> SuggestionResult = string.IsNullOrEmpty(FileName)
                                                                                                        ? ParentFolder.GetChildItemsAsync(SettingPage.IsDisplayHiddenItemsEnabled, SettingPage.IsDisplayProtectedSystemItemsEnabled)
                                                                                                        : ParentFolder.GetChildItemsAsync(SettingPage.IsDisplayHiddenItemsEnabled, SettingPage.IsDisplayProtectedSystemItemsEnabled, AdvanceFilter: (Name) => Name.StartsWith(FileName, StringComparison.OrdinalIgnoreCase));

                                        await foreach (AddressSuggestionItem Item in SuggestionResult.Take(20).Select((Item) => new AddressSuggestionItem(Item.Path, Item.DisplayName, Visibility.Collapsed)))
                                        {
                                            if (!args.CheckCurrent())
                                            {
                                                break;
                                            }

                                            AddressSuggestionList.Add(Item);
                                        }
                                    }
                                }
                                else
                                {
                                    IReadOnlyList<VariableDataPackage> VariablePathList = await EnvironmentVariables.GetVariablePathListAsync();
                                    IAsyncEnumerable<FileSystemStorageItemBase> SuggestionResult = AsyncEnumerable.Empty<FileSystemStorageItemBase>();

                                    foreach (string Path in VariablePathList.Select((Var) => Var.Path))
                                    {
                                        if (!args.CheckCurrent())
                                        {
                                            break;
                                        }

                                        if (await FileSystemStorageItemBase.OpenAsync(Path) is FileSystemStorageFolder VariableFolder)
                                        {
                                            SuggestionResult = SuggestionResult.Concat(VariableFolder.GetChildItemsAsync(SettingPage.IsDisplayHiddenItemsEnabled, SettingPage.IsDisplayProtectedSystemItemsEnabled, AdvanceFilter: (Name) => Name.StartsWith(TargetPath, StringComparison.OrdinalIgnoreCase)));
                                        }
                                    }

                                    if (args.CheckCurrent())
                                    {
                                        await foreach (AddressSuggestionItem Item in SuggestionResult.Distinct()
                                                                                                     .Take(20)
                                                                                                     .Select((Item) => new AddressSuggestionItem(Item.Path, Item.DisplayName, Visibility.Collapsed)))
                                        {
                                            if (!args.CheckCurrent())
                                            {
                                                break;
                                            }

                                            AddressSuggestionList.Add(Item);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTracer.Log(ex, "Could not generate the suggestion list of address box");
                    }
                });
            }
        }

        private async void GoParentFolder_Click(object sender, RoutedEventArgs e)
        {
            string CurrentFolderPath = CurrentPresenter?.CurrentFolder?.Path;

            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                string DirectoryPath = Path.GetDirectoryName(CurrentFolderPath);

                if (string.IsNullOrEmpty(DirectoryPath) && !CurrentFolderPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
                {
                    DirectoryPath = RootVirtualFolder.Current.Path;
                }

                if (await CurrentPresenter.DisplayItemsInFolderAsync(DirectoryPath))
                {
                    if (CurrentPresenter.FileCollection.OfType<FileSystemStorageFolder>().FirstOrDefault((Item) => Item.Path.Equals(CurrentFolderPath, StringComparison.OrdinalIgnoreCase)) is FileSystemStorageItemBase Folder)
                    {
                        CurrentPresenter.SelectedItem = Folder;
                        CurrentPresenter.ItemPresenter.ScrollIntoView(Folder, ScrollIntoViewAlignment.Leading);
                    }
                }
                else
                {
                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{DirectoryPath}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };

                    await Dialog.ShowAsync();
                }
            }
        }

        private async void GoBackRecord_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteGoBackActionIfAvailableAsync();
        }

        private async void GoForwardRecord_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteGoForwardActionIfAvailableAsync();
        }

        private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
        {
            AddressButtonContainer.Visibility = Visibility.Collapsed;
        }

        private void AddressBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!((e.OriginalSource as TextBox)?.ContextFlyout?.IsOpen).GetValueOrDefault())
            {
                AddressButtonContainer.Visibility = Visibility.Visible;
            }
        }

        private async void AddressButton_Click(object sender, RoutedEventArgs e)
        {
            Button Btn = sender as Button;

            if (Btn.DataContext is AddressBlock Block
                && !string.IsNullOrEmpty(CurrentPresenter?.CurrentFolder?.Path)
                && !Block.Path.Equals(CurrentPresenter.CurrentFolder.Path, StringComparison.OrdinalIgnoreCase)
                && (Block.Path.StartsWith(@"\\?\") || !Block.Path.StartsWith(@"\") || Block.Path.Split(@"\", StringSplitOptions.RemoveEmptyEntries).Length > 1))
            {
                if (!await CurrentPresenter.DisplayItemsInFolderAsync(Block.Path))
                {
                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{Block.Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };

                    await Dialog.ShowAsync();
                }
            }
        }

        private async void AddressExtension_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button Btn)
            {
                AddressExtensionList.Clear();

                if (Btn.DataContext is AddressBlock Block && Btn.Content is FrameworkElement DropDownElement)
                {
                    try
                    {
                        if (Block.Path.Equals(RootVirtualFolder.Current.Path))
                        {
                            AddressExtensionList.AddRange(CommonAccessCollection.DriveList.Select((Drive) => Drive.DriveFolder));
                        }
                        else if (await FileSystemStorageItemBase.OpenAsync(Block.Path) is FileSystemStorageFolder Folder)
                        {
                            AddressExtensionCancellation?.Cancel();
                            AddressExtensionCancellation?.Dispose();
                            AddressExtensionCancellation = new CancellationTokenSource();

                            IReadOnlyList<FileSystemStorageItemBase> ChildItems = await Folder.GetChildItemsAsync(SettingPage.IsDisplayHiddenItemsEnabled, SettingPage.IsDisplayProtectedSystemItemsEnabled, CancelToken: AddressExtensionCancellation.Token, Filter: BasicFilters.Folder).ToListAsync();

                            if (ChildItems.Count > 0)
                            {
                                AddressExtensionList.AddRange(await SortedCollectionGenerator.GetSortedCollectionAsync(ChildItems.Cast<FileSystemStorageFolder>(), SortTarget.Name, SortDirection.Ascending, SortStyle.UseFileSystemStyle));
                            }
                        }

                        if (AddressExtensionList.Count > 0)
                        {
                            Vector2 RotationCenter = new Vector2(Convert.ToSingle(DropDownElement.ActualWidth * 0.45), Convert.ToSingle(DropDownElement.ActualHeight * 0.57));

                            await AnimationBuilder.Create().CenterPoint(RotationCenter, RotationCenter).RotationInDegrees(90, duration: TimeSpan.FromMilliseconds(150)).StartAsync(DropDownElement);

                            FlyoutBase.SetAttachedFlyout(Btn, AddressExtensionFlyout);
                            FlyoutBase.ShowAttachedFlyout(Btn);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        //No need to handle this exception
                    }
                    catch (Exception ex)
                    {
                        LogTracer.Log(ex, "Could not generate the extension for address block");
                    }
                }
            }
        }

        private async void AddressExtensionFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
        {
            AddressExtensionList.Clear();

            if ((sender.Target as Button)?.Content is FrameworkElement DropDownElement)
            {
                Vector2 RotationCenter = new Vector2(Convert.ToSingle(DropDownElement.ActualWidth * 0.45), Convert.ToSingle(DropDownElement.ActualHeight * 0.57));

                await AnimationBuilder.Create().CenterPoint(RotationCenter, RotationCenter).RotationInDegrees(0, duration: TimeSpan.FromMilliseconds(150)).StartAsync(DropDownElement);
            }
        }

        private async void AddressExtensionSubFolderList_ItemClick(object sender, ItemClickEventArgs e)
        {
            AddressExtensionFlyout.Hide();

            if (e.ClickedItem is FileSystemStorageFolder TargetFolder)
            {
                if (!await CurrentPresenter.DisplayItemsInFolderAsync(TargetFolder))
                {
                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{TargetFolder.Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };

                    await Dialog.ShowAsync();
                }
            }
        }

        private async void AddressButton_Drop(object sender, DragEventArgs e)
        {
            Button Btn = sender as Button;

            if (Btn.DataContext is AddressBlock Block && !Block.Path.Equals(RootVirtualFolder.Current.Path, StringComparison.OrdinalIgnoreCase))
            {
                DragOperationDeferral Deferral = e.GetDeferral();

                try
                {
                    e.Handled = true;

                    DelayEnterCancel?.Cancel();

                    IReadOnlyList<string> PathList = await e.DataView.GetAsStorageItemPathListAsync();

                    if (PathList.Count > 0)
                    {
                        if (e.AcceptedOperation.HasFlag(DataPackageOperation.Move))
                        {
                            if (PathList.All((Path) => !System.IO.Path.GetDirectoryName(Path).Equals(Block.Path, StringComparison.OrdinalIgnoreCase)))
                            {
                                QueueTaskController.EnqueueMoveOpeartion(new OperationListMoveModel(PathList.ToArray(), Block.Path));
                            }
                        }
                        else
                        {
                            QueueTaskController.EnqueueCopyOpeartion(new OperationListCopyModel(PathList.ToArray(), Block.Path));
                        }
                    }
                }
                catch (Exception ex) when (ex.HResult is unchecked((int)0x80040064) or unchecked((int)0x8004006A))
                {
                    if (await FileSystemStorageItemBase.OpenAsync(Block.Path) is not (null or INotWin32StorageFolder))
                    {
                        QueueTaskController.EnqueueRemoteCopyOpeartion(new OperationListRemoteModel(Block.Path));
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"An exception was threw in {nameof(AddressButton_Drop)}");

                    await new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_FailToGetClipboardError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    }.ShowAsync();
                }
                finally
                {
                    Deferral.Complete();
                }
            }
        }

        private void AddressButton_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                e.Handled = true;
                e.AcceptedOperation = DataPackageOperation.None;

                if ((sender as FrameworkElement)?.DataContext is AddressBlock Block)
                {
                    if (e.DataView.Contains(StandardDataFormats.StorageItems)
                        || e.DataView.Contains(ExtendedDataFormats.CompressionItems)
                        || e.DataView.Contains(ExtendedDataFormats.NotSupportedStorageItem)
                        || e.DataView.Contains(ExtendedDataFormats.FileDrop))
                    {
                        if (!RootVirtualFolder.Current.Path.Equals(Block.Path, StringComparison.OrdinalIgnoreCase))
                        {
                            e.DragUIOverride.IsContentVisible = true;
                            e.DragUIOverride.IsGlyphVisible = true;
                            e.DragUIOverride.IsCaptionVisible = true;

                            if (e.AllowedOperations.HasFlag(DataPackageOperation.Copy) && e.AllowedOperations.HasFlag(DataPackageOperation.Move))
                            {
                                if (e.Modifiers.HasFlag(DragDropModifiers.Control))
                                {
                                    e.AcceptedOperation = DataPackageOperation.Move;
                                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_MoveTo")} \"{Block.DisplayName}\"";
                                }
                                else
                                {
                                    e.AcceptedOperation = DataPackageOperation.Copy;
                                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_CopyTo")} \"{Block.DisplayName}\"";
                                }
                            }
                            else if (e.AllowedOperations.HasFlag(DataPackageOperation.Copy))
                            {
                                e.AcceptedOperation = DataPackageOperation.Copy;
                                e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_CopyTo")} \"{Block.DisplayName}\"";
                            }
                            else if (e.AllowedOperations.HasFlag(DataPackageOperation.Move))
                            {
                                e.AcceptedOperation = DataPackageOperation.Move;
                                e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_MoveTo")} \"{Block.DisplayName}\"";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw in {nameof(AddressButton_DragOver)}");
            }
        }

        private async void FolderCut_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuFlyout.Hide();

            string Path = (FolderTree.SelectedNode?.Content as TreeViewNodeContent)?.Path;

            if (await FileSystemStorageItemBase.OpenAsync(Path) is FileSystemStorageFolder Folder)
            {
                try
                {
                    Clipboard.Clear();

                    DataPackage Package = new DataPackage
                    {
                        RequestedOperation = DataPackageOperation.Move,
                    };

                    await Package.SetStorageItemDataAsync(Folder);

                    Clipboard.SetContent(Package);
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex);

                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnableAccessClipboard_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    await Dialog.ShowAsync().ConfigureAwait(false);
                }
            }
            else
            {
                await new CommonContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                }.ShowAsync();
            }
        }

        private async void FolderCopy_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuFlyout.Hide();

            string Path = (FolderTree.SelectedNode?.Content as TreeViewNodeContent)?.Path;

            if (await FileSystemStorageItemBase.OpenAsync(Path) is FileSystemStorageFolder Folder)
            {
                try
                {
                    Clipboard.Clear();

                    DataPackage Package = new DataPackage
                    {
                        RequestedOperation = DataPackageOperation.Copy
                    };

                    await Package.SetStorageItemDataAsync(Folder);

                    Clipboard.SetContent(Package);
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex);

                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnableAccessClipboard_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    await Dialog.ShowAsync().ConfigureAwait(false);
                }
            }
            else
            {
                await new CommonContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                }.ShowAsync();
            }
        }

        private async void OpenFolderInNewWindow_Click(object sender, RoutedEventArgs e)
        {
            if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
            {
                if (await FileSystemStorageItemBase.CheckExistsAsync(Content.Path))
                {
                    string StartupArgument = Uri.EscapeDataString(JsonConvert.SerializeObject(new List<string[]>
                    {
                        new string[]{ Content.Path }
                    }));

                    await Launcher.LaunchUriAsync(new Uri($"rx-explorer-uwp:{StartupArgument}"));
                }
                else
                {
                    await new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    }.ShowAsync();
                }
            }
        }

        private async void OpenFolderInNewTab_Click(object sender, RoutedEventArgs e)
        {
            string Path = (FolderTree.SelectedNode?.Content as TreeViewNodeContent)?.Path;

            if (await FileSystemStorageItemBase.CheckExistsAsync(Path))
            {
                await TabViewContainer.Current.CreateNewTabAsync(Path);
            }
            else
            {
                await new CommonContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                }.ShowAsync();
            }
        }

        private void SearchEngineConfirm_Click(object sender, RoutedEventArgs e)
        {
            SearchEngineFlyout.Hide();

            if (SearchInDefaultEngine.IsChecked.GetValueOrDefault())
            {
                Frame.Navigate(typeof(SearchPage), new SearchOptions
                {
                    SearchFolder = CurrentPresenter.CurrentFolder,
                    IgnoreCase = BuiltInEngineIgnoreCase.IsChecked.GetValueOrDefault(),
                    UseRegexExpression = BuiltInEngineIncludeRegex.IsChecked.GetValueOrDefault(),
                    UseAQSExpression = BuiltInEngineIncludeAQS.IsChecked.GetValueOrDefault(),
                    DeepSearch = BuiltInSearchAllSubFolders.IsChecked.GetValueOrDefault() || CurrentPresenter.CurrentFolder is RootVirtualFolder,
                    SearchText = GlobeSearch.Text,
                    EngineCategory = SearchCategory.BuiltInEngine
                }, AnimationController.Current.IsEnableAnimation ? new DrillInNavigationTransitionInfo() : new SuppressNavigationTransitionInfo());
            }
            else
            {
                Frame.Navigate(typeof(SearchPage), new SearchOptions
                {
                    SearchFolder = CurrentPresenter.CurrentFolder,
                    IgnoreCase = EverythingEngineIgnoreCase.IsChecked.GetValueOrDefault(),
                    UseRegexExpression = EverythingEngineIncludeRegex.IsChecked.GetValueOrDefault(),
                    DeepSearch = EverythingEngineSearchGloble.IsChecked.GetValueOrDefault() || CurrentPresenter.CurrentFolder is RootVirtualFolder,
                    SearchText = GlobeSearch.Text,
                    EngineCategory = SearchCategory.EverythingEngine
                }, AnimationController.Current.IsEnableAnimation ? new DrillInNavigationTransitionInfo() : new SuppressNavigationTransitionInfo());
            }
        }

        private void SearchEngineCancel_Click(object sender, RoutedEventArgs e)
        {
            SearchEngineFlyout.Hide();
        }

        private void SearchEngineFlyout_Opened(object sender, object e)
        {
            ShouldNotAcceptShortcutKeyInput = true;
            SearchEngineConfirm.Focus(FocusState.Programmatic);
        }

        private void SearchEngineFlyout_Closed(object sender, object e)
        {
            ShouldNotAcceptShortcutKeyInput = false;
        }

        private void SeachEngineOptionSave_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox Box)
            {
                switch (Box.Name)
                {
                    case "EverythingEngineIgnoreCase":
                        {
                            ApplicationData.Current.LocalSettings.Values["EverythingEngineIgnoreCase"] = true;
                            break;
                        }
                    case "EverythingEngineIncludeRegex":
                        {
                            ApplicationData.Current.LocalSettings.Values["EverythingEngineIncludeRegex"] = true;
                            break;
                        }
                    case "EverythingEngineSearchGloble":
                        {
                            ApplicationData.Current.LocalSettings.Values["EverythingEngineSearchGloble"] = true;
                            break;
                        }
                    case "BuiltInEngineIgnoreCase":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInEngineIgnoreCase"] = true;
                            break;
                        }
                    case "BuiltInEngineIncludeRegex":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInEngineIncludeRegex"] = true;
                            break;
                        }
                    case "BuiltInSearchAllSubFolders":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInSearchAllSubFolders"] = true;
                            break;
                        }
                    case "BuiltInEngineIncludeAQS":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInEngineIncludeAQS"] = true;
                            break;
                        }
                    case "BuiltInSearchUseIndexer":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInSearchUseIndexer"] = true;
                            break;
                        }
                }
            }
        }

        private void SeachEngineOptionSave_UnChecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox Box)
            {
                switch (Box.Name)
                {
                    case "EverythingEngineIgnoreCase":
                        {
                            ApplicationData.Current.LocalSettings.Values["EverythingEngineIgnoreCase"] = false;
                            break;
                        }
                    case "EverythingEngineIncludeRegex":
                        {
                            ApplicationData.Current.LocalSettings.Values["EverythingEngineIncludeRegex"] = false;
                            break;
                        }
                    case "EverythingEngineSearchGloble":
                        {
                            ApplicationData.Current.LocalSettings.Values["EverythingEngineSearchGloble"] = false;
                            break;
                        }
                    case "BuiltInEngineIgnoreCase":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInEngineIgnoreCase"] = false;
                            break;
                        }
                    case "BuiltInEngineIncludeRegex":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInEngineIncludeRegex"] = false;
                            break;
                        }
                    case "BuiltInSearchAllSubFolders":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInSearchAllSubFolders"] = false;
                            break;
                        }
                    case "BuiltInEngineIncludeAQS":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInEngineIncludeAQS"] = false;
                            break;
                        }
                    case "BuiltInSearchUseIndexer":
                        {
                            ApplicationData.Current.LocalSettings.Values["BuiltInSearchUseIndexer"] = false;
                            break;
                        }
                }
            }
        }

        private void SearchEngineChoiceSave_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton Btn)
            {
                switch (Btn.Name)
                {
                    case "SearchInDefaultEngine":
                        {
                            ApplicationData.Current.LocalSettings.Values["DefaultSearchEngine"] = Enum.GetName(typeof(SearchCategory), SearchCategory.BuiltInEngine);
                            break;
                        }
                    case "SearchInEverythingEngine":
                        {
                            ApplicationData.Current.LocalSettings.Values["DefaultSearchEngine"] = Enum.GetName(typeof(SearchCategory), SearchCategory.EverythingEngine);
                            break;
                        }
                }
            }
        }

        private void SearchEngineFlyout_Opening(object sender, object e)
        {
            if (CurrentPresenter.CurrentFolder is INotWin32StorageFolder)
            {
                BuiltInSearchAllSubFolders.Visibility = Visibility.Visible;

                SearchInDefaultEngine.Checked -= SearchEngineChoiceSave_Checked;
                SearchInDefaultEngine.IsChecked = true;
                SearchInDefaultEngine.Checked += SearchEngineChoiceSave_Checked;

                SearchInEverythingEngine.Checked -= SearchEngineChoiceSave_Checked;
                SearchInEverythingEngine.IsChecked = false;
                SearchInEverythingEngine.Checked -= SearchEngineChoiceSave_Checked;

                BuiltInEngineIncludeAQS.Visibility = Visibility.Collapsed;
                BuiltInEngineIncludeAQS.Checked -= SeachEngineOptionSave_Checked;
                BuiltInEngineIncludeAQS.IsChecked = false;
                BuiltInEngineIncludeAQS.Checked += SeachEngineOptionSave_Checked;

                BuiltInSearchUseIndexer.Visibility = Visibility.Collapsed;
                BuiltInSearchUseIndexer.Checked -= SeachEngineOptionSave_Checked;
                BuiltInSearchUseIndexer.IsChecked = false;
                BuiltInSearchUseIndexer.Checked += SeachEngineOptionSave_Checked;

                SearchOptions Options = SearchOptions.LoadSavedConfiguration(SearchCategory.BuiltInEngine);

                BuiltInEngineIgnoreCase.IsChecked = Options.IgnoreCase;
                BuiltInEngineIncludeRegex.IsChecked = Options.UseRegexExpression;
                BuiltInSearchAllSubFolders.IsChecked = Options.DeepSearch;
            }
            else
            {
                if (CurrentPresenter.CurrentFolder is RootVirtualFolder)
                {
                    BuiltInEngineIncludeAQS.Visibility = Visibility.Visible;
                    BuiltInSearchUseIndexer.Visibility = Visibility.Visible;

                    BuiltInSearchAllSubFolders.Visibility = Visibility.Collapsed;
                    BuiltInSearchAllSubFolders.Checked -= SeachEngineOptionSave_Checked;
                    BuiltInSearchAllSubFolders.IsChecked = true;
                    BuiltInSearchAllSubFolders.Checked += SeachEngineOptionSave_Checked;

                    EverythingEngineSearchGloble.Visibility = Visibility.Collapsed;
                    EverythingEngineSearchGloble.Checked -= SeachEngineOptionSave_Checked;
                    EverythingEngineSearchGloble.IsChecked = true;
                    EverythingEngineSearchGloble.Checked += SeachEngineOptionSave_Checked;
                }
                else
                {
                    BuiltInSearchAllSubFolders.Visibility = Visibility.Visible;
                    BuiltInEngineIncludeAQS.Visibility = Visibility.Visible;
                    BuiltInSearchUseIndexer.Visibility = Visibility.Visible;
                    EverythingEngineSearchGloble.Visibility = Visibility.Visible;
                }

                if (ApplicationData.Current.LocalSettings.Values.TryGetValue("DefaultSearchEngine", out object Choice))
                {
                    switch (Enum.Parse<SearchCategory>(Convert.ToString(Choice)))
                    {
                        case SearchCategory.BuiltInEngine:
                        case SearchCategory.EverythingEngine when !SearchInEverythingEngine.IsEnabled:
                            {
                                SearchInDefaultEngine.IsChecked = true;
                                SearchInEverythingEngine.IsChecked = false;

                                SearchOptions Options = SearchOptions.LoadSavedConfiguration(SearchCategory.BuiltInEngine);

                                BuiltInEngineIgnoreCase.IsChecked = Options.IgnoreCase;
                                BuiltInEngineIncludeRegex.IsChecked = Options.UseRegexExpression;
                                BuiltInSearchAllSubFolders.IsChecked = Options.DeepSearch;
                                BuiltInEngineIncludeAQS.IsChecked = Options.UseAQSExpression;
                                BuiltInSearchUseIndexer.IsChecked = Options.UseIndexerOnly;

                                break;
                            }
                        case SearchCategory.EverythingEngine when SearchInEverythingEngine.IsEnabled:
                            {
                                SearchInEverythingEngine.IsChecked = true;
                                SearchInDefaultEngine.IsChecked = false;

                                SearchOptions Options = SearchOptions.LoadSavedConfiguration(SearchCategory.EverythingEngine);

                                EverythingEngineIgnoreCase.IsChecked = Options.IgnoreCase;
                                EverythingEngineIncludeRegex.IsChecked = Options.UseRegexExpression;
                                EverythingEngineSearchGloble.IsChecked = Options.DeepSearch;

                                break;
                            }
                    }
                }
                else
                {
                    SearchInDefaultEngine.IsChecked = true;
                    SearchInEverythingEngine.IsChecked = false;

                    SearchOptions Options = SearchOptions.LoadSavedConfiguration(SearchCategory.BuiltInEngine);

                    BuiltInEngineIgnoreCase.IsChecked = Options.IgnoreCase;
                    BuiltInEngineIncludeRegex.IsChecked = Options.UseRegexExpression;
                    BuiltInSearchAllSubFolders.IsChecked = Options.DeepSearch;
                    BuiltInEngineIncludeAQS.IsChecked = Options.UseAQSExpression;
                    BuiltInSearchUseIndexer.IsChecked = Options.UseIndexerOnly;
                }
            }
        }

        public async Task CreateNewBladeAsync(FileSystemStorageItemBase Item)
        {
            try
            {
                await CreateBladeExecution.ExecuteAsync(async () =>
                {
                    FilePresenter Presenter = new FilePresenter(this);

                    BladeItem Blade = new BladeItem
                    {
                        Content = Presenter,
                        IsExpanded = true,
                        Header = Item.DisplayName,
                        Background = new SolidColorBrush(Colors.Transparent),
                        TitleBarBackground = new SolidColorBrush(Colors.Transparent),
                        TitleBarVisibility = Visibility.Visible,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        VerticalContentAlignment = VerticalAlignment.Stretch
                    };

                    Blade.AddHandler(PointerPressedEvent, BladePointerPressedEventHandler, true);
                    Blade.Expanded += Blade_Expanded;
                    Blade.Collapsed += Blade_Collapsed;

                    if (BladeViewer.IsLoaded)
                    {
                        double BladeHeight = BladeViewer.ActualHeight;
                        double BladeWidth = (BladeViewer.ActualWidth - BladeViewer.Items.Cast<BladeItem>().Where((Item) => !Item.IsExpanded).Sum((Item) => Item.ActualWidth)) / Math.Min(BladeViewer.Items.Cast<BladeItem>().Where((Item) => Item.IsExpanded).Count() + 1, SettingPage.VerticalSplitViewLimitation);

                        Blade.Width = BladeWidth;
                        Blade.Height = BladeHeight;

                        if (BladeViewer.Items.Count > 0)
                        {
                            Blade.TitleBarVisibility = Visibility.Visible;

                            foreach (BladeItem Item in BladeViewer.Items)
                            {
                                Item.Height = BladeHeight;
                                Item.TitleBarVisibility = Visibility.Visible;

                                if (Item.IsExpanded)
                                {
                                    Item.Width = BladeWidth;
                                }
                            }
                        }
                        else
                        {
                            Blade.TitleBarVisibility = Visibility.Collapsed;
                        }
                    }

                    BladeViewer.Items.Add(Blade);

                    CurrentPresenter = Presenter;

                    switch (Item)
                    {
                        case FileSystemStorageFolder Folder:
                            {
                                if (!await Presenter.DisplayItemsInFolderAsync(Folder))
                                {
                                    goto default;
                                }

                                break;
                            }
                        case FileSystemStorageFile File:
                            {
                                string FolderPath = Path.GetDirectoryName(File.Path);

                                if (!string.IsNullOrEmpty(FolderPath))
                                {
                                    if (await Presenter.DisplayItemsInFolderAsync(FolderPath))
                                    {
                                        if (CurrentPresenter.FileCollection.FirstOrDefault((Item) => Item.Path.Equals(File.Path, StringComparison.OrdinalIgnoreCase)) is FileSystemStorageItemBase Item)
                                        {
                                            CurrentPresenter.SelectedItem = Item;
                                            CurrentPresenter.ItemPresenter.ScrollIntoView(Item, ScrollIntoViewAlignment.Leading);
                                        }

                                        break;
                                    }
                                }

                                goto default;
                            }
                        default:
                            {
                                CommonContentDialog Dialog = new CommonContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{Item.Path}\"",
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                                };

                                await Dialog.ShowAsync();

                                break;
                            }
                    }
                });
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "An exception was threw when creating new blade");
            }
        }

        private async void Blade_Collapsed(object sender, EventArgs e)
        {
            if (sender is BladeItem Item)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    Item.UpdateLayout();

                    double BladeHeight = BladeViewer.ActualHeight;
                    double BladeWidth = (BladeViewer.ActualWidth - BladeViewer.Items.Cast<BladeItem>().Where((Item) => !Item.IsExpanded).Sum((Item) => Item.ActualWidth)) / Math.Min(BladeViewer.Items.Cast<BladeItem>().Where((Item) => Item.IsExpanded).Count(), SettingPage.VerticalSplitViewLimitation);

                    foreach (BladeItem Item in BladeViewer.Items.Cast<BladeItem>())
                    {
                        Item.Height = BladeHeight;

                        if (Item.IsExpanded)
                        {
                            Item.Width = BladeWidth;
                        }
                    }

                    if (BladeViewer.Items.Count == 1)
                    {
                        if (BladeViewer.Items.Single() is BladeItem Item && Item.IsExpanded)
                        {
                            Item.TitleBarVisibility = Visibility.Collapsed;
                        }
                    }
                });
            }
        }

        private async void Blade_Expanded(object sender, EventArgs e)
        {
            if (sender is BladeItem Item)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    Item.UpdateLayout();

                    double BladeHeight = BladeViewer.ActualHeight;
                    double BladeWidth = (BladeViewer.ActualWidth - BladeViewer.Items.Cast<BladeItem>().Where((Item) => !Item.IsExpanded).Sum((Item) => Item.ActualWidth)) / Math.Min(BladeViewer.Items.Cast<BladeItem>().Where((Item) => Item.IsExpanded).Count(), SettingPage.VerticalSplitViewLimitation);

                    foreach (BladeItem Item in BladeViewer.Items)
                    {
                        Item.Height = BladeHeight;

                        if (Item.IsExpanded)
                        {
                            Item.Width = BladeWidth;
                        }
                    }

                    if (BladeViewer.Items.Count == 1)
                    {
                        if (BladeViewer.Items.Single() is BladeItem Item && Item.IsExpanded)
                        {
                            Item.TitleBarVisibility = Visibility.Collapsed;
                        }
                    }
                });
            }
        }

        private async void Blade_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (BladeViewer.Items.Count > 1
                && sender is BladeItem Blade && Blade.Content is FilePresenter Presenter
                && CurrentPresenter != Presenter)
            {
                CurrentPresenter = Presenter;

                if (Presenter.CurrentFolder is RootVirtualFolder)
                {
                    TabViewContainer.Current.LayoutModeControl.IsEnabled = false;
                }
                else
                {
                    string CurrentPath = Presenter.CurrentFolder?.Path;

                    if (!string.IsNullOrEmpty(CurrentPath))
                    {
                        PathConfiguration Config = SQLite.Current.GetPathConfiguration(CurrentPath);
                        TabViewContainer.Current.LayoutModeControl.IsEnabled = true;
                        TabViewContainer.Current.LayoutModeControl.CurrentPath = Config.Path;
                        TabViewContainer.Current.LayoutModeControl.ViewModeIndex = Config.DisplayModeIndex.GetValueOrDefault();

                        if (SettingPage.IsExpandTreeViewAsContentChanged)
                        {
                            if (FolderTree.RootNodes.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path.Equals(Path.GetPathRoot(CurrentPath), StringComparison.OrdinalIgnoreCase)) is TreeViewNode RootNode)
                            {
                                ExpandTreeViewCancellation?.Cancel();
                                ExpandTreeViewCancellation?.Dispose();
                                ExpandTreeViewCancellation = new CancellationTokenSource(15000);

                                if (await RootNode.GetTargetNodeAsync(new PathAnalysis(CurrentPath), true, ExpandTreeViewCancellation.Token) is TreeViewNode TargetNode)
                                {
                                    await FolderTree.SelectNodeAndScrollToVerticalAsync(TargetNode);
                                }
                            }
                        }
                    }
                }
            }
        }

        public async Task CloseBladeAsync(BladeItem Blade)
        {
            if (Blade.Content is FilePresenter Presenter)
            {
                Presenter.Dispose();

                Blade.RemoveHandler(PointerPressedEvent, BladePointerPressedEventHandler);
                Blade.Expanded -= Blade_Expanded;
                Blade.Collapsed -= Blade_Collapsed;
                Blade.Content = null;
            }

            BladeViewer.Items.Remove(Blade);

            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                if (BladeViewer.Items.LastOrDefault() is BladeItem Blade && Blade.Content is FilePresenter LastPresenter)
                {
                    CurrentPresenter = LastPresenter;
                }

                double BladeHeight = BladeViewer.ActualHeight;
                double BladeWidth = (BladeViewer.ActualWidth - BladeViewer.Items.Cast<BladeItem>().Where((Item) => !Item.IsExpanded).Sum((Item) => Item.ActualWidth)) / Math.Min(BladeViewer.Items.Cast<BladeItem>().Where((Item) => Item.IsExpanded).Count(), SettingPage.VerticalSplitViewLimitation);

                foreach (BladeItem Item in BladeViewer.Items)
                {
                    Item.Height = BladeHeight;

                    if (Item.IsExpanded)
                    {
                        Item.Width = BladeWidth;
                    }
                }

                if (BladeViewer.Items.Count == 1)
                {
                    if (BladeViewer.Items.Single() is BladeItem Item && Item.IsExpanded)
                    {
                        Item.TitleBarVisibility = Visibility.Collapsed;
                    }
                }
            });

        }

        private async void BladeViewer_BladeClosed(object sender, BladeItem e)
        {
            await CloseBladeAsync(e);
        }

        private void BladeViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                if (BladeViewer.Items.Count > 1)
                {
                    foreach (BladeItem Item in BladeViewer.Items.Cast<BladeItem>())
                    {
                        if (Item.Height != e.NewSize.Height)
                        {
                            Item.Height = e.NewSize.Height;
                        }

                        if (Item.IsExpanded)
                        {
                            double NewWidth = (e.NewSize.Width - BladeViewer.Items.Cast<BladeItem>().Where((Item) => !Item.IsExpanded).Sum((Item) => Item.ActualWidth)) / Math.Min(BladeViewer.Items.Cast<BladeItem>().Where((Item) => Item.IsExpanded).Count(), SettingPage.VerticalSplitViewLimitation);

                            if (Item.Width != NewWidth)
                            {
                                Item.Width = NewWidth;
                            }
                        }
                    }
                }
                else if (BladeViewer.Items.FirstOrDefault() is BladeItem Item)
                {
                    if (Item.Height != e.NewSize.Height)
                    {
                        Item.Height = e.NewSize.Height;
                    }

                    if (Item.Width != e.NewSize.Width)
                    {
                        Item.Width = e.NewSize.Width;
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not adjust the size of BladeItem");
            }
        }

        private void AddressButton_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Button Btn && Btn.DataContext is AddressBlock Item)
            {
                DelayEnterCancel?.Cancel();
                DelayEnterCancel?.Dispose();
                DelayEnterCancel = new CancellationTokenSource();

                Task.Delay(1500).ContinueWith(async (task, obj) =>
                {
                    try
                    {
                        if (obj is (AddressBlock Block, CancellationToken Token))
                        {
                            if (!Token.IsCancellationRequested)
                            {
                                await CurrentPresenter.OpenSelectedItemAsync(Block.Path);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTracer.Log(ex, "An exception was thew in DelayEnterProcess");
                    }
                }, (Item, DelayEnterCancel.Token), TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void AddressButton_DragLeave(object sender, DragEventArgs e)
        {
            DelayEnterCancel?.Cancel();
        }

        private void GridSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ((GridSplitter)sender).ReleasePointerCaptures();
        }

        private void GridSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            ((GridSplitter)sender).CapturePointer(e.Pointer);
        }

        private void Splitter_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            ((GridSplitter)sender).ReleasePointerCaptures();
        }

        private async void OpenFolderInVerticalSplitView_Click(object sender, RoutedEventArgs e)
        {
            if (FolderTree.SelectedNode?.Content is TreeViewNodeContent Content)
            {
                if (await FileSystemStorageItemBase.OpenAsync(Content.Path) is FileSystemStorageItemBase Item)
                {
                    await CreateNewBladeAsync(Item);
                }
            }
        }

        private void AddressBarSelectionDelete_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;

            if ((sender as FrameworkElement)?.DataContext is AddressSuggestionItem Item)
            {
                AddressSuggestionList.Remove(Item);
                SQLite.Current.DeletePathHistory(Item.Path);
            }
        }

        private async void GoHome_Click(object sender, RoutedEventArgs e)
        {
            await CurrentPresenter.DisplayItemsInFolderAsync(RootVirtualFolder.Current);
        }

        private void SearchSelectionDelete_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;

            if ((sender as FrameworkElement)?.DataContext is SearchSuggestionItem Item)
            {
                SearchSuggestionList.Remove(Item);
                SQLite.Current.DeleteSearchHistory(Item.Text);
            }
        }

        private void GoForwardRecord_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!CurrentPresenter.ForwardNavigationStack.IsEmpty)
            {
                DelayGoForwardHoldCancel?.Cancel();
                DelayGoForwardHoldCancel?.Dispose();
                DelayGoForwardHoldCancel = new CancellationTokenSource();

                Task.Delay(800).ContinueWith((task, input) =>
                {
                    try
                    {
                        if (input is CancellationToken Token && !Token.IsCancellationRequested)
                        {
                            NavigationRecordList.Clear();
                            NavigationRecordList.AddRange(CurrentPresenter.ForwardNavigationStack.Select((Item) => new NavigationRecordDisplay(Item.Path)));

                            FlyoutBase.SetAttachedFlyout(GoForwardRecord, AddressHistoryFlyout);
                            FlyoutBase.ShowAttachedFlyout(GoForwardRecord);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTracer.Log(ex, "An exception was thew in DelayEnterProcess");
                    }
                }, DelayGoForwardHoldCancel.Token, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void GoForwardRecord_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            DelayGoForwardHoldCancel?.Cancel();
        }


        private void GoBackRecord_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!CurrentPresenter.BackNavigationStack.IsEmpty)
            {
                DelayGoBackHoldCancel?.Cancel();
                DelayGoBackHoldCancel?.Dispose();
                DelayGoBackHoldCancel = new CancellationTokenSource();

                Task.Delay(800).ContinueWith((task, input) =>
                {
                    try
                    {
                        if (input is CancellationToken Token && !Token.IsCancellationRequested)
                        {
                            NavigationRecordList.Clear();
                            NavigationRecordList.AddRange(CurrentPresenter.BackNavigationStack.Select((Item) => new NavigationRecordDisplay(Item.Path)));

                            FlyoutBase.SetAttachedFlyout(GoBackRecord, AddressHistoryFlyout);
                            FlyoutBase.ShowAttachedFlyout(GoBackRecord);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTracer.Log(ex, "An exception was thew in DelayEnterProcess");
                    }
                }, DelayGoBackHoldCancel.Token, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void GoBackRecord_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            DelayGoBackHoldCancel?.Cancel();
        }

        private async void AddressNavigationHistoryFlyoutList_ItemClick(object sender, ItemClickEventArgs e)
        {
            AddressHistoryFlyout.Hide();

            if (e.ClickedItem is NavigationRecordDisplay Record && CurrentPresenter?.CurrentFolder is FileSystemStorageFolder CurrentFolder)
            {
                //We must check whether the record path is still exists first to avoid change the navigation stack but could not enter the folder later
                if (await FileSystemStorageItemBase.CheckExistsAsync(Record.Path))
                {
                    IReadOnlyList<string> SelectedItemPathList = CurrentPresenter.SelectedItems.Select((Item) => Item.Path).ToArray();

                    switch (AddressHistoryFlyout.Target.Name)
                    {
                        case nameof(GoBackRecord):
                            {
                                NavigationRelatedRecord[] Records = CurrentPresenter.BackNavigationStack.Take(NavigationRecordList.IndexOf(Record) + 1).ToArray();
                                CurrentPresenter.BackNavigationStack.TryPopRange(Records);
                                CurrentPresenter.ForwardNavigationStack.PushRange(Records.SkipLast(1).Prepend(new NavigationRelatedRecord
                                {
                                    Path = CurrentFolder.Path,
                                    SelectedItemPath = SelectedItemPathList.Count > 1 ? string.Empty : (SelectedItemPathList.SingleOrDefault() ?? string.Empty)
                                }).ToArray());

                                break;
                            }
                        case nameof(GoForwardRecord):
                            {
                                NavigationRelatedRecord[] Records = CurrentPresenter.ForwardNavigationStack.Take(NavigationRecordList.IndexOf(Record) + 1).ToArray();
                                CurrentPresenter.ForwardNavigationStack.TryPopRange(Records);
                                CurrentPresenter.BackNavigationStack.PushRange(Records.SkipLast(1).Prepend(new NavigationRelatedRecord
                                {
                                    Path = CurrentFolder.Path,
                                    SelectedItemPath = SelectedItemPathList.Count > 1 ? string.Empty : (SelectedItemPathList.SingleOrDefault() ?? string.Empty)
                                }).ToArray());

                                break;
                            }
                    }

                    if (!await CurrentPresenter.DisplayItemsInFolderAsync(Record.Path, true, true))
                    {
                        CommonContentDialog Dialog = new CommonContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{Record.Path}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };

                        await Dialog.ShowAsync();
                    }
                }
                else
                {
                    CommonContentDialog Dialog = new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} {Environment.NewLine}\"{Record.Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };

                    await Dialog.ShowAsync();
                }
            }
        }

        private async void AddQuickAccessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FolderPicker Picker = new FolderPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.ComputerFolder
                };
                Picker.FileTypeFilter.Add("*");

                if (await Picker.PickSingleFolderAsync() is StorageFolder Folder)
                {
                    if (CommonAccessCollection.LibraryList.Any((Library) => Library.Path.Equals(Folder.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        CommonContentDialog Dialog = new CommonContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_TipTitle"),
                            Content = Globalization.GetString("QueueDialog_RepeatAddToHomePage_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };

                        await Dialog.ShowAsync();
                    }
                    else if (!string.IsNullOrEmpty(Folder.Path))
                    {
                        if (await LibraryStorageFolder.CreateAsync(LibraryType.UserCustom, Folder.Path) is LibraryStorageFolder LibFolder)
                        {
                            CommonAccessCollection.LibraryList.Add(LibFolder);
                            SQLite.Current.SetLibraryPathRecord(LibraryType.UserCustom, Folder.Path);
                            await JumpListController.AddItemAsync(JumpListGroup.Library, Folder.Path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not add the folder to quick access");
            }
        }

        private void SendToFlyout_Opening(object sender, object e)
        {
            if (sender is MenuFlyout Flyout)
            {
                foreach (MenuFlyoutItem Item in Flyout.Items)
                {
                    Item.Click -= SendToItem_Click;
                }

                Flyout.Items.Clear();

                MenuFlyoutItem SendDocumentItem = new MenuFlyoutItem
                {
                    Name = "SendDocumentItem",
                    Text = Globalization.GetString("SendTo_Document"),
                    Icon = new ImageIcon
                    {
                        Source = new BitmapImage(new Uri("ms-appx:///Assets/DocumentIcon.ico"))
                    },
                    MinWidth = 150,
                    MaxWidth = 350
                };
                SendDocumentItem.Click += SendToItem_Click;

                Flyout.Items.Add(SendDocumentItem);

                if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
                {
                    if (!Regex.IsMatch(Content.Path, @"^((ftps?:\\{1,2}[^\\]+.*)|(\\\\\?\\[^\\]+.*))", RegexOptions.IgnoreCase))
                    {
                        MenuFlyoutItem SendLinkItem = new MenuFlyoutItem
                        {
                            Name = "SendLinkItem",
                            Text = Globalization.GetString("SendTo_CreateDesktopShortcut"),
                            Icon = new ImageIcon
                            {
                                Source = new BitmapImage(new Uri("ms-appx:///Assets/DesktopIcon.ico"))
                            },
                            MinWidth = 150,
                            MaxWidth = 350
                        };
                        SendLinkItem.Click += SendToItem_Click;

                        Flyout.Items.Add(SendLinkItem);
                    }

                    DriveDataBase[] RemovableDriveList = CommonAccessCollection.DriveList.Where((Drive) => (Drive.DriveType is DriveType.Removable or DriveType.Network)
                                                                                                            && !string.IsNullOrEmpty(Drive.Path)
                                                                                                            && !Content.Path.StartsWith(Drive.Path, StringComparison.OrdinalIgnoreCase)).ToArray();

                    for (int i = 0; i < RemovableDriveList.Length; i++)
                    {
                        DriveDataBase RemovableDrive = RemovableDriveList[i];

                        MenuFlyoutItem SendRemovableDriveItem = new MenuFlyoutItem
                        {
                            Name = $"SendRemovableItem{i}",
                            Text = $"{(string.IsNullOrEmpty(RemovableDrive.DisplayName) ? RemovableDrive.Path : RemovableDrive.DisplayName)}",
                            Icon = new ImageIcon
                            {
                                Source = RemovableDrive.Thumbnail
                            },
                            MinWidth = 150,
                            MaxWidth = 350,
                            Tag = RemovableDrive.Path
                        };
                        SendRemovableDriveItem.Click += SendToItem_Click;

                        Flyout.Items.Add(SendRemovableDriveItem);
                    }
                }
            }
        }

        private async void SendToItem_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuFlyout.Hide();

            if (sender is FrameworkElement Item)
            {
                if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
                {
                    switch (Item.Name)
                    {
                        case "SendLinkItem":
                            {
                                string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                                if (await FileSystemStorageItemBase.CheckExistsAsync(DesktopPath))
                                {
                                    using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
                                    {
                                        try
                                        {
                                            await Exclusive.Controller.CreateLinkAsync(new LinkFileData
                                            {
                                                LinkPath = Path.Combine(DesktopPath, $"{Path.GetFileName(Content.Path)}.lnk"),
                                                LinkTargetPath = Content.Path
                                            });
                                        }
                                        catch (Exception)
                                        {
                                            CommonContentDialog Dialog = new CommonContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_UnauthorizedCreateNewFile_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            await Dialog.ShowAsync();
                                        }
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        IReadOnlyList<User> UserList = await User.FindAllAsync();

                                        UserDataPaths DataPath = UserList.FirstOrDefault((User) => User.AuthenticationStatus == UserAuthenticationStatus.LocallyAuthenticated && User.Type == UserType.LocalUser) is User CurrentUser
                                                                 ? UserDataPaths.GetForUser(CurrentUser)
                                                                 : UserDataPaths.GetDefault();

                                        if (await FileSystemStorageItemBase.CheckExistsAsync(DataPath.Desktop))
                                        {
                                            using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
                                            {
                                                try
                                                {
                                                    await Exclusive.Controller.CreateLinkAsync(new LinkFileData
                                                    {
                                                        LinkPath = Path.Combine(DataPath.Desktop, $"{Path.GetFileName(Content.Path)}.lnk"),
                                                        LinkTargetPath = Content.Path
                                                    });
                                                }
                                                catch (Exception)
                                                {
                                                    CommonContentDialog Dialog = new CommonContentDialog
                                                    {
                                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                        Content = Globalization.GetString("QueueDialog_UnauthorizedCreateNewFile_Content"),
                                                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                    };

                                                    await Dialog.ShowAsync();
                                                }
                                            }
                                        }
                                        else
                                        {
                                            LogTracer.Log($"Could not execute \"Send to\" command because desktop path \"{DataPath.Desktop}\" is not exists");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogTracer.Log(ex, "Could not get desktop path from UserDataPaths");
                                    }
                                }

                                break;
                            }
                        case "SendDocumentItem":
                            {
                                string DocumentPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                                if (await FileSystemStorageItemBase.CheckExistsAsync(DocumentPath))
                                {
                                    QueueTaskController.EnqueueCopyOpeartion(new OperationListCopyModel(new string[] { Content.Path }, DocumentPath));
                                }
                                else
                                {
                                    try
                                    {
                                        IReadOnlyList<User> UserList = await User.FindAllAsync();

                                        UserDataPaths DataPath = UserList.FirstOrDefault((User) => User.AuthenticationStatus == UserAuthenticationStatus.LocallyAuthenticated && User.Type == UserType.LocalUser) is User CurrentUser
                                                                 ? UserDataPaths.GetForUser(CurrentUser)
                                                                 : UserDataPaths.GetDefault();

                                        if (await FileSystemStorageItemBase.CheckExistsAsync(DataPath.Documents))
                                        {
                                            QueueTaskController.EnqueueCopyOpeartion(new OperationListCopyModel(new string[] { Content.Path }, DataPath.Documents));
                                        }
                                        else
                                        {
                                            LogTracer.Log($"Could not execute \"Send to\" command because document path \"{DataPath.Documents}\" is not exists");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogTracer.Log(ex, "Could not get document path from UserDataPaths");
                                    }
                                }

                                break;
                            }
                        default:
                            {
                                if (Item.Tag is string RemovablePath)
                                {
                                    QueueTaskController.EnqueueCopyOpeartion(new OperationListCopyModel(new string[] { Content.Path }, RemovablePath));
                                }

                                break;
                            }
                    }
                }
            }
        }

        private async void RemovePin_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuFlyout.Hide();

            if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
            {
                if (CommonAccessCollection.LibraryList.FirstOrDefault((Lib) => Lib.Path.Equals(Content.Path, StringComparison.OrdinalIgnoreCase)) is LibraryStorageFolder TargetLib)
                {
                    SQLite.Current.DeleteLibraryFolderRecord(Content.Path);
                    CommonAccessCollection.LibraryList.Remove(TargetLib);
                    await JumpListController.RemoveItemAsync(JumpListGroup.Library, TargetLib.Path);
                }
            }
        }

        private void AddressBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Tab:
                    {
                        string[] PathList = AddressSuggestionList.Select((Item) => string.IsNullOrEmpty(Item.DisplayName) ? Item.Path : Item.DisplayName).ToArray();

                        if (PathList.Length > 0)
                        {
                            int Index = Array.IndexOf(PathList, AddressBox.Text);

                            if (Index >= 0)
                            {
                                AddressBox.Text = PathList[(Index + 1) % PathList.Length];
                            }
                            else
                            {
                                AddressBox.Text = PathList[0];
                            }
                        }

                        e.Handled = true;

                        break;
                    }
            }
        }

        private void BladeViewer_Loaded(object sender, RoutedEventArgs e)
        {
            if (BladeViewer.Items.Count > 1)
            {
                double BladeHeight = BladeViewer.ActualHeight;
                double BladeWidth = (BladeViewer.ActualWidth - BladeViewer.Items.Cast<BladeItem>().Where((Item) => !Item.IsExpanded).Sum((Item) => Item.ActualWidth)) / Math.Min(BladeViewer.Items.Cast<BladeItem>().Where((Item) => Item.IsExpanded).Count(), SettingPage.VerticalSplitViewLimitation);

                foreach (BladeItem Item in BladeViewer.Items)
                {
                    Item.Height = BladeHeight;
                    Item.TitleBarVisibility = Visibility.Visible;

                    if (Item.IsExpanded)
                    {
                        Item.Width = BladeWidth;
                    }
                }
            }
            else if (BladeViewer.Items.FirstOrDefault() is BladeItem Item)
            {
                Item.Height = BladeViewer.ActualHeight;
                Item.Width = BladeViewer.ActualWidth;
                Item.TitleBarVisibility = Visibility.Collapsed;
            }
        }

        private void FolderTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement Element
                && Element.Name != "ExpandCollapseChevron"
                && Element.FindParentOfType<TreeViewItem>() is TreeViewItem Item)
            {
                Item.IsExpanded = !Item.IsExpanded;
            }
        }

        private void EverythingQuestion_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            EverythingTip.IsOpen = true;
        }

        public async Task PrepareContextMenuAsync(CommandBarFlyout Flyout)
        {
            if (Flyout == ContextMenuFlyout)
            {
                if (FolderTree.RootNodes.FirstOrDefault((Node) => Node.Content == TreeViewNodeContent.QuickAccessNode) is TreeViewNode QuickAccessNode)
                {
                    AppBarButton RemovePinButton = Flyout.PrimaryCommands.OfType<AppBarButton>().First((Btn) => Btn.Name == "RemovePinButton");

                    if (FolderTree.SelectedNode is TreeViewNode Node && Node.CanTraceToRootNode(QuickAccessNode))
                    {
                        RemovePinButton.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        RemovePinButton.Visibility = Visibility.Collapsed;
                    }
                }

                if (Flyout.SecondaryCommands.OfType<AppBarButton>().FirstOrDefault((Item) => Item.Name == "OpenFolderInNewWindowButton") is AppBarButton NewWindowButton)
                {
                    if (FolderTree.SelectedNode is TreeViewNode Node && Node.Content is TreeViewNodeContent Content)
                    {
                        if (Regex.IsMatch(Content.Path, @"^((ftps?:\\{1,2}[^\\]+.*)|(\\\\\?\\[^\\]+.*))", RegexOptions.IgnoreCase))
                        {
                            NewWindowButton.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            NewWindowButton.Visibility = Visibility.Visible;
                        }
                    }
                }

                if (await MSStoreHelper.CheckPurchaseStatusAsync())
                {
                    Flyout.SecondaryCommands.OfType<AppBarButton>().First((Btn) => Btn.Name == "OpenFolderInVerticalSplitView").Visibility = Visibility.Visible;
                }
            }
        }

        private async void FolderTree_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            args.Handled = true;

            if (args.TryGetPosition(sender, out Point Position))
            {
                try
                {
                    if ((args.OriginalSource as FrameworkElement)?.DataContext is TreeViewNode Node)
                    {
                        FolderTree.SelectedNode = Node;

                        if (Node.Content is TreeViewNodeContent Content)
                        {
                            if (Content == TreeViewNodeContent.QuickAccessNode)
                            {
                                QuickAccessFlyout.ShowAt(FolderTree, new FlyoutShowOptions
                                {
                                    Position = Position,
                                    Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                                    ShowMode = FlyoutShowMode.Standard
                                });
                            }
                            else
                            {
                                AppBarButton FolderCopyButton = ContextMenuFlyout.PrimaryCommands.OfType<AppBarButton>().First((Btn) => Btn.Name == "FolderCopyButton");
                                AppBarButton FolderCutButton = ContextMenuFlyout.PrimaryCommands.OfType<AppBarButton>().First((Btn) => Btn.Name == "FolderCutButton");
                                AppBarButton FolderDeleteButton = ContextMenuFlyout.PrimaryCommands.OfType<AppBarButton>().First((Btn) => Btn.Name == "FolderDeleteButton");
                                AppBarButton FolderRenameButton = ContextMenuFlyout.PrimaryCommands.OfType<AppBarButton>().First((Btn) => Btn.Name == "FolderRenameButton");
                                AppBarButton FolderSendToButton = ContextMenuFlyout.SecondaryCommands.OfType<AppBarButton>().First((Btn) => Btn.Name == "FolderSendToButton");

                                if (FolderTree.RootNodes.Contains(Node))
                                {
                                    FolderCopyButton.IsEnabled = false;
                                    FolderCutButton.IsEnabled = false;
                                    FolderDeleteButton.IsEnabled = false;
                                    FolderRenameButton.IsEnabled = false;
                                    FolderSendToButton.Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    FolderCopyButton.IsEnabled = true;
                                    FolderCutButton.IsEnabled = true;
                                    FolderDeleteButton.IsEnabled = true;
                                    FolderRenameButton.IsEnabled = true;
                                    FolderSendToButton.Visibility = Visibility.Visible;
                                }

                                ContextMenuCancellation?.Cancel();
                                ContextMenuCancellation?.Dispose();
                                ContextMenuCancellation = new CancellationTokenSource();

                                if (await FileSystemStorageItemBase.OpenAsync(Content.Path) is not LabelCollectionVirtualFolder)
                                {
                                    for (int RetryCount = 0; RetryCount < 3; RetryCount++)
                                    {
                                        try
                                        {
                                            await PrepareContextMenuAsync(ContextMenuFlyout);
                                            await ContextMenuFlyout.ShowCommandBarFlyoutWithExtraContextMenuItems(FolderTree,
                                                                                                               Position,
                                                                                                               ContextMenuCancellation.Token,
                                                                                                               Content.Path);
                                            break;
                                        }
                                        catch (Exception)
                                        {
                                            ContextMenuFlyout = CreateNewFolderContextMenu();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not locate the folder in TreeView");

                    await new CommonContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    }.ShowAsync();
                }
            }
        }

        public void Dispose()
        {
            if (Execution.CheckAlreadyExecuted(this))
            {
                throw new ObjectDisposedException(nameof(FileControl));
            }

            GC.SuppressFinalize(this);

            Execution.ExecuteOnce(this, () =>
            {
                AddressButtonList.Clear();

                FolderTree.RootNodes.Clear();

                BladeViewer.Items.Cast<BladeItem>().Select((Blade) => Blade.Content).Cast<FilePresenter>().ForEach((Presenter) => Presenter.Dispose());
                BladeViewer.Items.Clear();

                CommonAccessCollection.DriveChanged -= CommonAccessCollection_DriveChanged;
                CommonAccessCollection.LibraryChanged -= CommonAccessCollection_LibraryChanged;
                AppThemeController.Current.ThemeChanged -= Current_ThemeChanged;

                GoBackRecord.RemoveHandler(PointerPressedEvent, GoBackButtonPressedHandler);
                GoBackRecord.RemoveHandler(PointerReleasedEvent, GoBackButtonReleasedHandler);
                GoForwardRecord.RemoveHandler(PointerPressedEvent, GoForwardButtonPressedHandler);
                GoForwardRecord.RemoveHandler(PointerReleasedEvent, GoForwardButtonReleasedHandler);

                GoBackRecord.IsEnabled = false;
                GoForwardRecord.IsEnabled = false;
                GoParentFolder.IsEnabled = false;

                DelayEnterCancel?.Dispose();
                DelayGoBackHoldCancel?.Dispose();
                DelayGoForwardHoldCancel?.Dispose();
                ContextMenuCancellation?.Dispose();
                AddressExtensionCancellation?.Dispose();
                ExpandTreeViewCancellation?.Dispose();

                DelayEnterCancel = null;
                DelayGoBackHoldCancel = null;
                DelayGoForwardHoldCancel = null;
                ContextMenuCancellation = null;
                AddressExtensionCancellation = null;
                ExpandTreeViewCancellation = null;
            });
        }

        ~FileControl()
        {
            Dispose();
        }
    }
}
