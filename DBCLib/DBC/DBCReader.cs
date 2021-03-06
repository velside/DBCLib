﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DBCLib.Exceptions;

namespace DBCLib
{
    internal class DBCReader<T> where T : class, new()
    {
        internal void ReadDBC(DBCFile<T> dbcFile, BinaryReader reader, DBCInfo info)
        {
            if (reader is null)
                return;

            // Validate the DBC fields
            var fields = dbcFile.GetDBCType().GetFields();
            int fieldCounts = dbcFile.FieldCount(fields, dbcFile.GetDBCType());
            if (info.DBCFields != fieldCounts)
                throw new InvalidDBCFields(dbcFile.GetDBCType().ToString());

            // We don't need to read the first bytes again (signature, dbcRecords, dbcFields, recordSize & stringSize)
            long headerSize = reader.BaseStream.Position;

            // Set position of reader
            reader.BaseStream.Position = info.DBCRecords * info.RecordSize + headerSize;

            var stringData = reader.ReadBytes((int)info.StringSize);
            string fullString = Encoding.UTF8.GetString(stringData);
            var strings = fullString.Split(new[] { '\0' }, StringSplitOptions.None);

            var stringTable = new Dictionary<int, string>();
            var currentPosition = 0;
            foreach (string str in strings)
            {
                stringTable.Add(currentPosition, str);
                currentPosition += Encoding.UTF8.GetByteCount(str) + 1;
            }

            // Reset position to base position
            reader.BaseStream.Position = headerSize;

            // Loop through all of the records in the DBC file
            for (uint i = 0; i < info.DBCRecords; ++i)
            {
                var instance = Activator.CreateInstance(dbcFile.GetDBCType());

                foreach (var field in fields)
                {
                    switch (Type.GetTypeCode(field.FieldType))
                    {
                        case TypeCode.Object:
                        {
                            if (field.FieldType == typeof(LocalizedString))
                            {
                                var value = "";
                                for (uint j = 0; j < LocalizedString.Size - 1; ++j)
                                {
                                    int offsetKey = reader.ReadInt32();
                                    if (value == "" && offsetKey != 0 && stringTable.TryGetValue(offsetKey, out string stringFromTable))
                                    {
                                        value = stringFromTable;
                                        dbcFile.LocalPosition = j;
                                    }
                                }

                                dbcFile.LocalFlag = reader.ReadUInt32();

                                field.SetValue(instance, (LocalizedString)value);
                            }
                            else if (field.FieldType.IsArray)
                            {
                                Array array;
                                int arrayLength;

                                switch (Type.GetTypeCode(field.FieldType.GetElementType()))
                                {
                                    case TypeCode.Int32:
                                        // Get length of array
                                        arrayLength = ((int[])field.GetValue(instance)).Length;

                                        // Set Array
                                        array = new int[arrayLength];

                                        // Set Value of DBC object by looping through the array
                                        for (var j = 0; j < arrayLength; ++j)
                                            array.SetValue(reader.ReadInt32(), j);
                                        field.SetValue(instance, array);
                                        break;
                                    case TypeCode.UInt32:
                                        // Get length of array
                                        arrayLength = ((uint[])field.GetValue(instance)).Length;

                                        // Set Array
                                        array = new uint[arrayLength];

                                        // Set Value of DBC object by looping through the array
                                        for (var j = 0; j < arrayLength; ++j)
                                            array.SetValue(reader.ReadUInt32(), j);
                                        field.SetValue(instance, array);
                                        break;
                                    case TypeCode.Single:
                                        // Get length of array
                                        arrayLength = ((float[])field.GetValue(instance)).Length;

                                        // Set Array
                                        array = new float[arrayLength];

                                        // Set Value of DBC object by looping through the array
                                        for (var j = 0; j < arrayLength; ++j)
                                            array.SetValue(reader.ReadSingle(), j);
                                        field.SetValue(instance, array);
                                        break;
                                    default:
                                        throw new NotImplementedException(Type.GetTypeCode(field.FieldType.GetElementType()).ToString());
                                }
                            }
                            break;
                        }
                        case TypeCode.Byte:
                        {
                            byte value = reader.ReadByte();
                            field.SetValue(instance, value);
                            break;
                        }
                        case TypeCode.Int32:
                        {
                            int value = reader.ReadInt32();
                            field.SetValue(instance, value);
                            break;
                        }
                        case TypeCode.UInt32:
                        {
                            uint value = reader.ReadUInt32();
                            field.SetValue(instance, value);
                            break;
                        }
                        case TypeCode.Single:
                        {
                            float value = reader.ReadSingle();
                            field.SetValue(instance, value);
                            break;
                        }
                        case TypeCode.String:
                        {
                            // Get offset for string table
                            int offsetKey = reader.ReadInt32();

                            // Check if offset exists in the string table
                            if (!stringTable.TryGetValue(offsetKey, out string stringFromTable))
                                throw new KeyNotFoundException(offsetKey.ToString());

                            string value = stringFromTable;
                            field.SetValue(instance, value);
                            break;
                        }
                        default:
                            throw new NotImplementedException(Type.GetTypeCode(field.FieldType).ToString());
                    }
                }

                // Get the first value of the DBC file and use that as key for the DBC record
                var firstValue = fields[0].GetValue(instance);
                var key = (uint)Convert.ChangeType(firstValue, typeof(uint));
                dbcFile.AddEntry(key, (T)instance);
            }
        }
    }
}
