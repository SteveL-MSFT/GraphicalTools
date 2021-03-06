// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NStack;
using OutGridView.Models;
using Terminal.Gui;

namespace OutGridView.Cmdlet
{
    internal class ConsoleGui : IDisposable
    {
        private const string FILTER_LABEL = "Filter";
        private const string APPLY_LABEL = "Apply";
        private bool _cancelled;
        private GridViewDataSource _itemSource;
        private ListView _listView;
        private ApplicationData _applicationData;
        private GridViewDetails _gridViewDetails;

        public HashSet<int> Start(ApplicationData applicationData)
        {
            Application.Init();
            _applicationData = applicationData;
            _gridViewDetails = new GridViewDetails
            {
                // If we have PassThru, then we want to make them selectable. If we make them selectable,
                // they have a 8 character addition of a checkbox ("     [ ]") that we have to factor in.
                ListViewOffset = _applicationData.PassThru ? 8 : 4
            };

            AddMenu();
            Window win = AddTopLevelWindow();

            // GridView header logic
            List<string> gridHeaders = _applicationData.DataTable.DataColumns.Select((c) => c.Label).ToList();
            CalculateColumnWidths(gridHeaders);

            AddFilter(win);
            AddHeaders(win, gridHeaders);

            // GridView row logic
            LoadData();
            AddRows(win);
            
            // Run the GUI.
            Application.Run();

            // Return results of selection if required.
            HashSet<int> selectedIndexes = new HashSet<int>();
            if (_cancelled || !_applicationData.PassThru)
            {
                return selectedIndexes;
            }

            foreach (GridViewRow gvr in _itemSource.GridViewRowList)
            {
                if (gvr.IsMarked)
                {
                    selectedIndexes.Add(gvr.OriginalIndex);
                }
            }

            return selectedIndexes;
        }

        private Window AddTopLevelWindow()
        {
            // Creates the top-level window to show
            var win = new Window(_applicationData.Title)
            {
                X = 0,
                Y = 1, // Leave one row for the toplevel menu
                // By using Dim.Fill(), it will automatically resize without manual intervention
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            Application.Top.Add(win);
            return win;
        }

        private void AddMenu()
        {
            var menu = new MenuBar(new MenuBarItem []
            {
                new MenuBarItem("_Actions (F9)", 
                    _applicationData.PassThru
                    ? new MenuItem []
                    {
                        new MenuItem("_Accept", string.Empty, () => { Application.RequestStop(); }),
                        new MenuItem("_Cancel", string.Empty, () =>{ _cancelled = true; Application.RequestStop(); })
                    }
                    : new MenuItem []
                    {
                        new MenuItem("_Close", string.Empty, () =>{ Application.RequestStop(); })
                    })
            });

            Application.Top.Add(menu);
        }

        private void CalculateColumnWidths(List<string> gridHeaders)
        {
            _gridViewDetails.ListViewColumnWidths = new int[gridHeaders.Count];
            var listViewColumnWidths = _gridViewDetails.ListViewColumnWidths;

            for (int i = 0; i < gridHeaders.Count; i++)
            {
                listViewColumnWidths[i] = gridHeaders[i].Length;
            }

            // calculate the width of each column based on longest string in each column for each row
            foreach (var row in _applicationData.DataTable.Data)
            {
                int index = 0;

                // use half of the visible buffer height for the number of objects to inspect to calculate widths
                foreach (var col in row.Values.Take(Application.Top.Frame.Height / 2))
                {
                    var len = col.Value.DisplayValue.Length;
                    if (len > listViewColumnWidths[index])
                    {
                        listViewColumnWidths[index] = len;
                    }
                    
                    index++;
                }
            }

            // if the total width is wider than the usable width, remove 1 from widest column until it fits
            // the gui loses 3 chars on the left and 2 chars on the right
            _gridViewDetails.UsableWidth = Application.Top.Frame.Width - 3 - listViewColumnWidths.Length - _gridViewDetails.ListViewOffset - 2;
            int columnWidthsSum = listViewColumnWidths.Sum();
            while (columnWidthsSum >= _gridViewDetails.UsableWidth)
            {
                int maxWidth = 0;
                int maxIndex = 0;
                for (int i = 0; i < listViewColumnWidths.Length; i++)
                {
                    if (listViewColumnWidths[i] > maxWidth)
                    {
                        maxWidth = listViewColumnWidths[i];
                        maxIndex = i;
                    }
                }

                listViewColumnWidths[maxIndex]--;
                columnWidthsSum--;
            }
        }

        private void AddFilter(Window win)
        {
            var filterLabel = new Label(FILTER_LABEL)
            {
                X = 2
            };

            // 2 is for the square brackets added to buttons
            var filterFieldWidth = _gridViewDetails.UsableWidth - filterLabel.Text.Length - APPLY_LABEL.Length - 2;
            var filterField = new TextField(string.Empty)
            {
                X = Pos.Right(filterLabel) + 1,
                Y = Pos.Top(filterLabel),
                CanFocus = true,
                Width = filterFieldWidth
            };

            var filterErrorLabel = new Label(string.Empty)
            {
                X = Pos.Right(filterLabel) + 1,
                Y = Pos.Top(filterLabel) + 1,
                ColorScheme = Colors.Base,
                Width = filterFieldWidth
            };

            EventHandler<ustring> filterChanged = (object sender, ustring e) =>
            {
                // TODO: remove Apply button and code when this starts working
                try
                {
                    filterErrorLabel.Text = " ";
                    filterErrorLabel.ColorScheme = Colors.Base;
                    filterErrorLabel.Redraw(filterErrorLabel.Bounds);

                    var itemList = GridViewHelpers.FilterData(_itemSource.GridViewRowList, e.ToString());
                    _listView.Source = new GridViewDataSource(itemList);
                }
                catch (Exception ex)
                {
                    filterErrorLabel.Text = ex.Message;
                    filterErrorLabel.ColorScheme = Colors.Error;
                    filterErrorLabel.Redraw(filterErrorLabel.Bounds);
                    _listView.Source = _itemSource;
                }
            };

            filterField.Changed += filterChanged;

            var filterApplyButton = new Button(APPLY_LABEL)
            {
                // Pos.Right(filterField) returns 0
                X = Pos.Right(filterLabel) + filterFieldWidth + 2,
                Y = Pos.Top(filterLabel),
                Clicked = () =>
                {
                    filterChanged.Invoke(null, filterField.Text);
                }
            };

            win.Add(filterLabel, filterField, filterErrorLabel, filterApplyButton);
        }

        private void AddHeaders(Window win, List<string> gridHeaders)
        {
            var header = new Label(GridViewHelpers.GetPaddedString(
                gridHeaders,
                _gridViewDetails.ListViewOffset + _gridViewDetails.ListViewOffset - 1,
                _gridViewDetails.ListViewColumnWidths))
            {
                X = 0,
                Y = 2
            };

            win.Add(header);

            // This renders dashes under the header to make it more clear what is header and what is data
            var headerLineText = new StringBuilder();
            foreach (char c in header.Text)
            {
                if (c.Equals(' '))
                {
                    headerLineText.Append(' ');
                }
                else
                {
                    // When gui.cs supports text decorations, should replace this with just underlining the header
                    headerLineText.Append('-');
                }
            }

            var headerLine = new Label(headerLineText.ToString())
            {
                X = 0,
                Y = 3
            };

            win.Add(headerLine);
        }

        private void LoadData()
        {
            var items = new List<GridViewRow>();
            int newIndex = 0;
            for (int i = 0; i < _applicationData.DataTable.Data.Count; i++)
            {
                var dataTableRow = _applicationData.DataTable.Data[i];
                var valueList = new List<string>();
                foreach (var dataTableColumn in _applicationData.DataTable.DataColumns)
                {
                    string dataValue = dataTableRow.Values[dataTableColumn.ToString()].DisplayValue;
                    valueList.Add(dataValue);
                }

                string displayString = GridViewHelpers.GetPaddedString(valueList, _gridViewDetails.ListViewOffset, _gridViewDetails.ListViewColumnWidths);

                items.Add(new GridViewRow
                {
                    DisplayString = displayString,
                    OriginalIndex = i
                });

                newIndex++;
            }

            _itemSource = new GridViewDataSource(items);
        }

        private void AddRows(Window win)
        {
            _listView = new ListView(_itemSource)
            {
                X = 3,
                Y = 4,
                Width = Dim.Fill(2),
                Height = Dim.Fill(2),
                AllowsMarking = _applicationData.PassThru
            };

            win.Add(_listView);
        }

        public void Dispose()
        {
            // By emitting this, we fix an issue where arrow keys don't work in the console
            // because .NET requires application mode to support Arrow key escape sequences
            // Esc[?1h - Set cursor key to application mode
            // See http://ascii-table.com/ansi-escape-sequences-vt-100.php
            Console.Write("\u001b[?1h");
        }
    }
}
