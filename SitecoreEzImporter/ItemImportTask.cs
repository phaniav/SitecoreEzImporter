﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using EzImporter.Extensions;

namespace EzImporter
{
    public class ItemImportTask
    {
        protected ItemImportTaskArgs Args { get; set; }
        protected StringBuilder Log { get; set; }

        public ItemImportTask()
        {
            Log = new StringBuilder();
        }

        public string Run(ItemImportTaskArgs args)
        {
            Args = args;
            ValidateArgs();
            var dataTable = new DataTable();
            ReadMapInfo(ref dataTable);
            ReadData(ref dataTable);
            ImportItems(dataTable);
            return Log.ToString();
        }

        protected bool ValidateArgs()
        {
            Log.AppendLine("Validating input...");
            var argsValid = true;
            if (!File.Exists(Args.FileName))
            {
                Log.AppendFormat("Input file '{0}' not found.{1}", Args.FileName, Environment.NewLine);
                argsValid = false;
            }
            return argsValid;
        }

        protected void ReadMapInfo(ref DataTable dataTable)
        {
            Log.AppendLine("Processing import map...");
            dataTable.Columns.Clear();
            foreach (var column in Args.Map.InputFields)
            {
                dataTable.Columns.Add(column.Name, typeof (string));
            }
            Log.AppendFormat("{0} Columns defined in map. {1}", Args.Map.InputFields.Count, Environment.NewLine);
        }

        protected void ReadData(ref DataTable dataTable)
        {
            DataReaders.IDataReader reader;
            if (Args.FileName.ToLower().EndsWith(".csv"))
            {
                reader = new DataReaders.CsvDataReader();
            }
            //else if (Args.FileName.ToLower().EndsWith(".xlsx"))
            //{
            //    reader = new DataReaders.XlsxDataReader();
            //}
            else
            {
                Log.AppendLine("Unsupported file format supplied. DataImporter accepts *.CSV and *.XLSX files");
                return;
            }
            reader.ReadData(ref dataTable, Args, Log);
        }

        protected void ImportItems(DataTable dataTable)
        {
            using (new LanguageSwitcher(Args.TargetLanguage))
            {
                var parentItem = Args.Database.GetItem(Args.RootItemId);
                foreach (var outputMap in Args.Map.OutputMaps)
                {
                    ImportMapItems(dataTable, outputMap, parentItem, true);
                }
            }
        }

        private void ImportMapItems(DataTable dataTable, OutputMap outputMap, Item parentItem, bool rootLevel)
        {
            var groupedTable = dataTable.GroupBy(outputMap.Fields.Select(f => f.SourceColumn).ToArray());
            for (int i = 0; i < groupedTable.Rows.Count; i++)
            {
                var row = groupedTable.Rows[i];
                if (rootLevel ||
                    Convert.ToString(row[outputMap.ParentMap.NameInputField]) == parentItem.Name)
                {
                    var createdItem = CreateNewItem(row, outputMap, parentItem);
                    if (createdItem != null &&
                        outputMap.ChildMaps != null && outputMap.ChildMaps.Any())
                    {
                        foreach (var childMap in outputMap.ChildMaps)
                        {
                            ImportMapItems(dataTable, childMap, createdItem, false);
                        }
                    }
                }
            }
        }

        protected Item CreateNewItem(DataRow dataRow, OutputMap outputMap, Item parentItem)
        {
            //CustomItemBase nItemTemplate = GetNewItemTemplate(dataRow);
            var templateItem = Args.Database.GetTemplate(outputMap.TemplateId);

            using (new LanguageSwitcher(Args.TargetLanguage))
            {
                //get the parent in the specific language
                Item parent = Args.Database.GetItem(parentItem.ID);

                Item newItem;
                //search for the child by name
                string newItemName = Utils.GetValidItemName(dataRow[outputMap.NameInputField]);
                newItem = parent.GetChildren()[newItemName];
                if (newItem != null)
                {
                    //add version for lang
                    newItem = newItem.Versions.AddVersion();
                    Log.AppendFormat("Creating new version of item {0}{1}", newItem.Paths.ContentPath, Environment.NewLine);
                }
                else
                {
                    //if not found then create one
                    newItem = parent.Add(newItemName, templateItem);
                    Log.AppendFormat("Creating item {0}{1}", newItem.Paths.ContentPath, Environment.NewLine);
                }

                if (newItem == null)
                {
                    throw new NullReferenceException("the new item created was null");
                }

                using (new EditContext(newItem, true, false))
                {
                    //add in the field mappings
                    for (int i = 0; i < outputMap.Fields.Count; i++)
                    {
                        var mapFieldName = outputMap.Fields[i].TargetFieldName;
                        if (!string.IsNullOrEmpty(mapFieldName))
                        {
                            var newItemField = newItem.Fields[mapFieldName];
                            if (newItemField != null)
                            {
                                var fieldValue = dataRow[outputMap.Fields[i].SourceColumn].ToString();
                                newItemField.Value = fieldValue;
                                Log.AppendFormat("'{0}' field set to '{1}'{2}", mapFieldName, fieldValue, Environment.NewLine);
                            }
                            else
                            {
                                Log.AppendFormat("Field '{0}' not found on item, skipping update for this field", mapFieldName);
                            }
                        }
                        else
                        {
                            Log.AppendLine("Null or empty mapping field name found");
                        }
                    }
                }
                return newItem;
            }
        }
    }
}
