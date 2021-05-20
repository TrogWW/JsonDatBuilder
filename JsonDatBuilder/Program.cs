using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.IO;
using BinaryEncoding;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace JsonDatBuilder
{
    public static class DataExtensions
    {
        /// <summary>
        /// Checks to see if string is a cheat code
        /// Codes must me space delimited and each delimited value must be 8 characters and be a hex value
        /// </summary>
        /// <returns></returns>
        public static bool IsCode(string str, out List<int> codeList)
        {
            codeList = null;
            string[] codeStringSplit = str.Split(' ');
            if (codeStringSplit.Length % 2 == 0 && codeStringSplit.Where(s => s.Length != 8).Count() == 0)
            {
                codeList = new List<int>();
                CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");
                for (int i = 0; i < codeStringSplit.Length; i++)
                {
                    string codeLine = codeStringSplit[i];
                    int codeInt = 0;

                    if (int.TryParse(codeLine, System.Globalization.NumberStyles.HexNumber, culture, out codeInt))
                    {
                        codeList.Add(codeInt);
                    }
                    else
                    {
                        codeList = null;
                        return false;
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Searches byte array for a available block of memory
        /// </summary>
        /// <param name="b">byte array</param>
        /// <param name="start_offset">first index of byte array to start looking</param>
        /// <param name="size">number of bytes required to be available</param>
        /// <returns></returns>
        public static int FirstAvailableSize(byte[] b, int start_offset, int size)
        {
            int remainder = start_offset % 4;
            start_offset = start_offset - remainder;
            for (int i = start_offset; i < b.Length; i = i + 4)
            {
                bool isFree = true;
                for (int j = i; j < i + size; j++)
                {
                    if (b[j] != 0)
                    {
                        isFree = false;
                        break;
                    }
                }
                if (isFree)
                {
                    return i;
                }
            }
            throw new Exception("Not enough memory");
        }
        /// <summary>
        /// Reserves memory by blocking it with 0xff
        /// </summary>
        public static void FillBytes(byte[] b, int start_offset, int end_offset)
        {
            for (int i = start_offset; i < end_offset; i++)
            {
                b[i] = 0xff;
            }
        }
    }
    public static class C_DataType_Helpers
    {
        /// <summary>
        /// Creates C_DataType based on object type
        /// </summary>
        public static C_DataType Create(object o)
        {
            if (o is JArray arr)
            {
                return new ListProperty(arr);
            }
            else if (o is JProperty prop)
            {
                return null;
            }
            else if (o is JObject obj)
            {
                return new ObjectProperty(obj);
            }
            else if (o is KeyValuePair<string, JToken> objProp)
            {
                return C_DataType_Helpers.Create(objProp.Value);
            }
            else if (o is JValue val)
            {
                if (val.Value is String strVal)
                {
                    List<int> codeList;
                    if (strVal.Length == 1)
                    {
                        return new CharProperty(strVal[0]);
                    }
                    else if (DataExtensions.IsCode(strVal, out codeList))
                    {
                        return new ListProperty(JArray.FromObject(codeList));
                    }
                    else
                    {
                        return new StringProperty(strVal);

                    }
                }
                else if (val.Value == null)
                {
                    return new IntProperty(0);
                }
                else if (val.Value is Int32 int32Val)
                {
                    return new IntProperty((int)int32Val);
                }
                else if (val.Value is Int64 int64Val)
                {
                    return new IntProperty((int)int64Val);
                }
                else if (val.Value is Double doubleVal)
                {
                    return new FloatProperty((float)doubleVal);
                }
                else if (val.Value is Boolean boolVal)
                {
                    int boolValInt = boolVal ? 1 : 0;
                    return new IntProperty((int)boolValInt);
                }
                else
                {
                    return null;
                }

            }
            return null;
        }
    }


    public interface C_DataType
    {
        int? address { get; set; }  //position of data in byte array
        int struct_size();          //data size (not including reference data)
        int total_size();           //data size( including reference data)
        void allocate_struct(byte[] b, int start_address);  //reserve memory in byte array for struct
        void fill_memory(byte[] b, ref int dynamic_memory_start);   //write struct data to byte array (may also write referenced data)
    }
    public class ObjectProperty : C_DataType
    {
        public int? address { get; set; }
        public List<C_DataType> properties;
        public ObjectProperty(JObject o)
        {
            properties = new List<C_DataType>();
            foreach (var prop in o)
            {
                properties.Add(C_DataType_Helpers.Create(prop));
            }
        }
        public int struct_size()
        {
            return properties.Sum(p => p.struct_size());
        }
        public int total_size()
        {
            return struct_size() + properties.Sum(p => p.total_size());
        }
        public void allocate_struct(byte[] b, int start_address)
        {
            this.address = start_address;//DataExtensions.FirstAvailableSize(b, start_address, struct_size());
            int curr_address = address.Value;
            foreach (var prop in properties)
            {
                prop.allocate_struct(b, curr_address);
                curr_address += prop.struct_size();
            }
            DataExtensions.FillBytes(b, this.address.Value, curr_address);
        }
        public void fill_memory(byte[] b, ref int dynamic_memory_start)
        {
            int curr_address = this.address.Value;
            foreach (var prop in this.properties)
            {
                prop.address = curr_address;
                curr_address += prop.struct_size();
            }
            DataExtensions.FillBytes(b, this.address.Value, curr_address);
            if (curr_address > dynamic_memory_start)
            {
                dynamic_memory_start = curr_address;
            }
            //dynamic_memory_start = curr_address;
            foreach (var prop in this.properties)
            {
                prop.fill_memory(b, ref dynamic_memory_start);
            }
        }

    }
    public class ListProperty : C_DataType
    {
        public int? address { get; set; }
        public int pointer_to_list;     //'pointer' to list. This is actually just the offset to the location of the data in the byte array
        public int list_size;
        public List<C_DataType> list;
        public ListProperty(JArray arr)
        {
            list = new List<C_DataType>();
            foreach (var item in arr)
            {
                list.Add(C_DataType_Helpers.Create(item));
            }
            list_size = list.Count;
        }
        public int struct_size()
        {
            return 8;
        }
        public int total_size()
        {
            return struct_size() + list.Sum(s => s.total_size());
        }
        public void allocate_struct(byte[] b, int start_address)
        {
            this.address = start_address;
            DataExtensions.FillBytes(b, this.address.Value, this.struct_size());
        }
        public void fill_memory(byte[] b, ref int dynamic_memory_start)
        {
            int elements_struct_size = this.list.Sum(l => l.struct_size());
            this.pointer_to_list = DataExtensions.FirstAvailableSize(b, dynamic_memory_start, elements_struct_size);

            Binary.BigEndian.Set(this.list.Count, b, address.Value);
            Binary.BigEndian.Set(this.pointer_to_list, b, address.Value + 4);


            DataExtensions.FillBytes(b, this.pointer_to_list, this.pointer_to_list + elements_struct_size);

            int curr_address = this.pointer_to_list;
            foreach (var item in list)
            {
                item.address = curr_address;
                curr_address += item.struct_size();
            }
            if (curr_address > dynamic_memory_start)
            {
                dynamic_memory_start = curr_address;
            }
            foreach (var item in list)
            {
                item.fill_memory(b, ref dynamic_memory_start);
            }
        }
    }
    public class CharProperty : C_DataType
    {
        public int? address { get; set; }
        public char value;
        public CharProperty(char value)
        {
            this.value = value;
        }
        public int struct_size()
        {
            return 1;
        }
        public int total_size()
        {
            return struct_size();
        }
        public void allocate_struct(byte[] b, int start_address)
        {
            this.address = start_address;
            DataExtensions.FillBytes(b, this.address.Value, this.address.Value + this.struct_size());
        }
        public void fill_memory(byte[] b, ref int dynamic_memory_start)
        {
            b[this.address.Value] = (byte)this.value;
        }
    }
    public class StringProperty : C_DataType
    {
        public int? address { get; set; }
        public int pointer_to_string;   //'pointer' to string. This is actually just the offset to the location of the data in the byte array
        public string value;
        public StringProperty(string value)
        {
            this.value = value;
        }
        public int struct_size()
        {
            return 4;
        }
        public int total_size()
        {
            return struct_size() + value.Length + 1;
        }
        public void allocate_struct(byte[] b, int start_address)
        {
            this.address = start_address;
            DataExtensions.FillBytes(b, this.address.Value, this.address.Value + this.struct_size());
        }
        public void fill_memory(byte[] b, ref int dynamic_memory_start)
        {
            int aligned_char_length = Program.GetAligned_u32(value.Length + 1);
            this.pointer_to_string = DataExtensions.FirstAvailableSize(b, dynamic_memory_start, aligned_char_length);
            DataExtensions.FillBytes(b, this.pointer_to_string, this.pointer_to_string + aligned_char_length);
            for (int i = 0; i < value.Length; i++)
            {
                b[i + this.pointer_to_string] = (byte)this.value[i];
            }
            b[value.Length + this.pointer_to_string] = (byte)'\0';

            Binary.BigEndian.Set(this.pointer_to_string, b, address.Value);
        }
    }
    public class IntProperty : C_DataType
    {
        public int? address { get; set; }
        public int property;
        public IntProperty(int property)
        {
            this.property = property;
        }
        public int struct_size()
        {
            return 4;
        }
        public int total_size()
        {
            return struct_size();
        }
        public void allocate_struct(byte[] b, int start_address)
        {
            this.address = start_address;
            DataExtensions.FillBytes(b, this.address.Value, this.address.Value + this.struct_size());
        }
        public void fill_memory(byte[] b, ref int dynamic_memory_start)
        {
            Binary.BigEndian.Set(property, b, address.Value);
        }
    }
    public class FloatProperty : C_DataType
    {
        public int? address { get; set; }
        public float property;
        public FloatProperty(float property)
        {
            this.property = property;
        }
        public int struct_size()
        {
            return 4;
        }
        public int total_size()
        {
            return struct_size();
        }
        public void allocate_struct(byte[] b, int start_address)
        {
            this.address = start_address;
            DataExtensions.FillBytes(b, this.address.Value, this.address.Value + this.struct_size());
        }
        public void fill_memory(byte[] b, ref int dynamic_memory_start)
        {
            var bytes = BitConverter.GetBytes(property).Reverse().ToArray();
            for (int i = 0; i < bytes.Length; i++)
            {
                b[i + address.Value] = bytes[i];
            }
        }
    }


    class Program
    {
        /// <summary>
        /// Returns the specified value but a multiple of 4 (size of int32) 
        /// </summary>
        public static int GetAligned_u32(int val)
        {
            int remainder = val % 4;
            val += 4 - remainder;
            return val;
        }
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                throw new Exception("Invalid arguments.");
            }
            string input_file_path = args[0];
            string output_file_path = args[1];
            if (input_file_path == null)
            {
                throw new Exception("Missing input file path.");
            }
            if (output_file_path == null)
            {
                throw new Exception("Missing output file path.");
            }
            if (!File.Exists(input_file_path))
            {
                throw new Exception("Unable to locate input file path.");
            }
            C_DataType c;
            JArray areas = JsonConvert.DeserializeObject<JArray>(File.ReadAllText(input_file_path));
            c = C_DataType_Helpers.Create(areas);

            int size = c.total_size();  //estimate total required size
            size = GetAligned_u32(size);
            byte[] b = new byte[size];

            c.allocate_struct(b, 0);

            int dynamic_memory_start = 0;
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] == 0)
                {
                    dynamic_memory_start = i;
                    break;
                }
            }

            c.fill_memory(b, ref dynamic_memory_start);

            //initial data size was an estimate. Reduce byte size if possible
            int last_byte_of_data = -1;
            for (int i = size - 1; i >= 0; i--)
            {
                if (b[i] != 0)
                {
                    last_byte_of_data = i;
                    break;
                }
            }
            last_byte_of_data += 32; //add 32 bytes of padding 0s for safety
            last_byte_of_data = GetAligned_u32(last_byte_of_data);
            last_byte_of_data = Math.Min(last_byte_of_data, size);
            Array.Resize(ref b, last_byte_of_data);


            File.WriteAllBytes(output_file_path, b);
        }
    }
}
