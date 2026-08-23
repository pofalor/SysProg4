using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SysProgTemplateShared.Structure;
using SysProgTemplateShared.Exceptions;
using System.ComponentModel.DataAnnotations;
using SysProgTemplateShared.Dto;
using SysProgTemplateShared.Helpers;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Reflection.Emit;
using System.Text.Json;
using System.Diagnostics.SymbolStore;


namespace SysProgTemplateShared
{
    public class Assembler 
    {
        public List<List<string>> SourceCode { get; set; } = new List<List<string>>();
        public List<string> BinaryCode { get; set; } = new List<string>();

        public int lineIterator = 0; 

        private const int maxAddress = 16777215;  // 2^24 - 1  
        private int startAddress = 0;
        private int endAddress = 0;
        private bool startFlag = false; 
        private bool endFlag = false; 
        private int ip = 0;


        public List<Command> AvailibleCommands { get; set; } = [
            new Command(){ Name = "JMP", Code = 1, Length = 4 },
            new Command(){ Name = "INT", Code = 2, Length = 4 },
            new Command(){ Name = "CALL", Code = 3, Length = 4 },
            new Command(){ Name = "ADD", Code = 4, Length = 2 },
            new Command(){ Name = "SUB", Code = 5, Length = 2 }
        ];

        private readonly string[] AvailibleDirectives = ["START", "END", "WORD", "BYTE", "RESB", "RESW"]; 

        public List<SymbolicName> TSI = new(); 

        public void SetAvailibleCommands(List<CommandDto> newAvailibleCommandsDto)
        {
            // try to convert 
            var newAvailibleCommands = newAvailibleCommandsDto.Select(c => new Command(c)).ToList();


            // check Name uniqueness 
            var nhs = new HashSet<string>();
            bool isNameUnique = newAvailibleCommands.All(x => nhs.Add(x.Name.ToUpper()));

            if (!isNameUnique)
                throw new AssemblerException("Все имена команд должны быть уникальными");

            bool isOverlapWithCommands = newAvailibleCommands.Any(c => IsDirective(c.Name.ToUpper()) || IsRegister(c.Name.ToUpper()));

            if (isOverlapWithCommands)
                throw new AssemblerException("Имена команд не должны совпадать с именами директив и регистров"); 


            // check Code uniqueness 
            var chs = new HashSet<int>();
            bool isCodeUnique = newAvailibleCommands.All(x => chs.Add(x.Code));

            if (!isCodeUnique)
                throw new AssemblerException("Все коды команд должны быть уникальными");


            this.AvailibleCommands = newAvailibleCommands; 
        }
        
        public void Reset(List<List<string>> sourceCode, List<CommandDto> newCommands)
        {
            SetAvailibleCommands(newCommands);
            ClearTSI();
            SourceCode = sourceCode;
            BinaryCode = new List<string>();

            startAddress = 0;
            endAddress = 0;
            startFlag = false;
            endFlag = false;
            ip = 0;
            lineIterator = 0;
        }

        public bool ProcessStep()
        {
            if (lineIterator == -1 || endFlag) return true; 

            var line = SourceCode[lineIterator]; 

            CodeLine codeLine = null;

            var textLine = string.Join(" ", line);
            var binaryCodeLine = string.Empty;

            if (!startFlag && ip != 0) throw new AssemblerException($"Не найдена директива START в начале программы");

            // overflow check 
            if (startFlag) OverflowCheck(ip, textLine);

            codeLine = GetCodeLineFromSource(line);

            // processing label first 
            if (codeLine.Label != null)
            {
                // try to find label in tsi 
                if (startFlag)
                {
                    var symbolicName = GetSymbolicName(codeLine.Label);

                    if (symbolicName == null)
                    {
                        symbolicName = new SymbolicName()
                        {
                            Name = codeLine.Label.ToUpper(),
                            Address = ip
                        }; 

                        TSI.Add(symbolicName);
                    }
                    else
                    {
                        if (symbolicName.Address == -1)
                        {
                            symbolicName.Address = ip;

                            ProvideAddresses(symbolicName);
                        }
                        else
                        {
                            throw new AssemblerException($"Такая метка уже есть в ТСИ: {textLine}");
                        }
                    }
                }
            }

            // processing command part
            // cannot be null, so no null check needed
            if (IsDirective(codeLine.Command))
            {
                switch (codeLine.Command)
                {
                    case "START":
                        {
                            Console.WriteLine(lineIterator);
                            if (codeLine.FirstOperand == null) throw new AssemblerException($"Не было задано значение адреса начала программы, но адрес начала программы не может быть равен нулю: {textLine}");

                            if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается один операнд, но найдено два: {textLine}");

                            // start should be at the beginning and first 
                            if (ip != 0 || startFlag) 
                                throw new AssemblerException($"START должен быть единственным, в начале исходного кода: {textLine}");

                            // start was found 
                            startFlag = true;

                            // process first operand
                            int address;

                            // check if it is a valid hex value 
                            try
                            {
                                address = Convert.ToInt32(codeLine.FirstOperand, 10);
                            }
                            catch
                            {
                                throw new AssemblerException($"Невозможно преобразовать первый операнд в адрес начала программы: {textLine}");
                            }

                            // check if it's within allocated memory bounds  
                            OverflowCheck(address, textLine);

                            if (address == 0) throw new AssemblerException($"Адрес начала программы не может быть равен нулю: {textLine}");

                            if (codeLine.Label == null) throw new AssemblerException($"Перед директивой START должна быть метка");

                            ip = address;
                            startAddress = address;

                            // output 
                            binaryCodeLine = $"{"H"} {codeLine.Label} {address:X6}";
                            break;
                        }

                    case "WORD":
                        // can only contain a 3-byte unsigned int value 
                        {
                            if (codeLine.FirstOperand == null) throw new AssemblerException($"Ожидается один операнд, но было получено ноль: {textLine}");
                            if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается один операнд, но найдено два: {textLine}");

                            int value;

                            // try convert 
                            try
                            {
                                value = Convert.ToInt32(codeLine.FirstOperand, 10);
                            }
                            catch (Exception ex)
                            {
                                throw new AssemblerException($"Невозможно преобразовать первый операнд в число: {textLine}");
                            }

                            // check if within 0-16777215 
                            if (value <= 0 || value > 16777215) throw new AssemblerException($"Значение первого операнда выходит за границы допустимого диапазона (1-16777215): {textLine}");

                            // check for allocated memory overflow 
                            OverflowCheck(ip + 3, textLine);

                            binaryCodeLine = $"{"T"} {ip:X6} {3} {value:X6}";
                            ip += 3;
                            break;
                        }

                    case "BYTE":
                        {
                            if (codeLine.FirstOperand == null) throw new AssemblerException($"Ожидается один операнд, но было получено ноль: {textLine}");
                            if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается один операнд, но найдено два: {textLine}");

                            int value;

                            // try to parse as a 1 byte value 
                            if (int.TryParse(codeLine.FirstOperand, out value))
                            {
                                // check if within 0-255 
                                if (value < 0 || value > 255) throw new AssemblerException($"Значение первого операнда выходит за границы допустимого диапазона (0-255): {textLine}");

                                // check for allocated memory overflow 
                                OverflowCheck(ip + 1, textLine);

                                binaryCodeLine = $"{"T"} {ip:X6} {1} {value:X2}";
                                ip += 1;
                                break;
                            }
                            // couldnt parse as a numeric value => parse as a character string 
                            else if (IsCString(codeLine.FirstOperand))
                            {
                                string symbols = codeLine.FirstOperand.Substring(2, codeLine.FirstOperand.Length - 3);

                                if (symbols.Length > 255)
                                    throw new AssemblerException($"Длина строки не может превышать 255 байт: {textLine}");

                                // check for allocated memory overflow 
                                OverflowCheck(ip + symbols.Length, textLine);

                                binaryCodeLine = $"{"T"} {ip:X6} {symbols.Length:X2} {ConvertToASCII(symbols)}";
                                ip += symbols.Length;
                                break;
                            }
                            else if (IsXString(codeLine.FirstOperand))
                            {
                                string symbols = codeLine.FirstOperand.Substring(2, codeLine.FirstOperand.Length - 3);

                                if (symbols.Length / 2 > 255)
                                    throw new AssemblerException($"Длина строки не может превышать 255 байт: {textLine}");

                                // check for allocated memory overflow 
                                OverflowCheck(ip + symbols.Length / 2, textLine);

                                binaryCodeLine = $"{"T"} {ip:X6} {symbols.Length / 2:X2} {symbols}";
                                ip += symbols.Length / 2;
                                break;
                            }
                            else
                            {
                                throw new AssemblerException($"Невозможно преобразовать первый операнд в символьную или шестнадцатеричную строку: {textLine}");
                            }
                        }

                    case "RESW":
                        {
                            if (codeLine.FirstOperand == null) throw new AssemblerException($"Ожидается один операнд, но было получено ноль: {textLine}");
                            if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается один операнд, но найдено два: {textLine}");

                            int value;

                            // try convert 
                            try
                            {
                                value = Convert.ToInt32(codeLine.FirstOperand, 10);
                            }
                            catch (Exception ex)
                            {
                                throw new AssemblerException($"Невозможно преобразовать первый операнд в число: {textLine}");
                            }

                            // check if within 0-16777215 
                            if (value <= 0 || value > 255) throw new AssemblerException($"Значение первого операнда выходит за границы допустимого диапазона (1-255): {textLine}");

                            // check for allocated memory overflow 
                            OverflowCheck(ip + value * 3, textLine);

                            binaryCodeLine = $"{"T"} {ip:X6} {value * 3:X2}";
                            ip += value * 3;
                            break;
                        }

                    case "RESB":
                        {
                            if (codeLine.FirstOperand == null) throw new AssemblerException($"Ожидается один операнд, но было получено ноль: {textLine}");
                            if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается один операнд, но найдено два: {textLine}");

                            int value;

                            // try convert 
                            try
                            {
                                value = Convert.ToInt32(codeLine.FirstOperand, 10);
                            }
                            catch (Exception ex)
                            {
                                throw new AssemblerException($"Невозможно преобразовать первый операнд в число: {textLine}");
                            }

                            // check if within 0-16777215 
                            if (value <= 0 || value > 255) throw new AssemblerException($"Значение первого операнда выходит за границы допустимого диапазона (1-255): {textLine}");

                            // check for allocated memory overflow 
                            OverflowCheck(ip + value, textLine);

                            binaryCodeLine = $"{"T"} {ip:X6} {value:X2}";
                            ip += value;
                            break;
                        }

                    case "END":
                        {
                            if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается максимум один операнд, но найдено два: {textLine}");

                            if (!startFlag || endFlag) throw new AssemblerException($"Не найдена метка START либо ошибка в директивах START/END: {textLine}");

                            if (codeLine.FirstOperand == null)
                            {
                                endAddress = startAddress;
                            }
                            else
                            {
                                int address;

                                // check if it is a valid hex value 
                                try
                                {
                                    address = Convert.ToInt32(codeLine.FirstOperand, 10);
                                }
                                catch
                                {
                                    throw new AssemblerException($"Невозможно преобразовать первый операнд в адрес входа в программу: {textLine}");
                                }

                                if (address < 0 || address > 16777215)
                                    throw new AssemblerException($"Значение первого операнда выходит за границы допустимого диапазона (0-16777215): {textLine}");

                                if (address < startAddress || address > ip)
                                    throw new AssemblerException($"Недопустимый адрес входа в программу {textLine}");

                                // check if it's within allocated memory bounds  
                                OverflowCheck(address, textLine);

                                endAddress = address;
                            }

                            var progLength = ip - startAddress;

                            BinaryCode[0] = $"{BinaryCode[0]} {progLength:X6}";

                            endFlag = true;

                            binaryCodeLine = $"{"E"} {endAddress:X6}";

                            CheckAddressRequirements(); 

                            break;
                        }
                }
            }

            // is it a command? 
            else if (IsCommand(codeLine.Command))
            {
                var command = AvailibleCommands.Find(c => c.Name.ToUpper() == codeLine.Command);

                switch (command.Length) 
                {
                    // length is 1 (operandless)
                    case 1:
                        {
                            if (codeLine.FirstOperand != null) throw new AssemblerException($"Ожидается ноль операндов: {textLine}");

                            // check for allocated memory overflow 
                            OverflowCheck(ip + 1, textLine);

                            // addressing type 00 
                            binaryCodeLine = $"{"T"} {ip:X6} {command.Length:X2} {(command.Code * 4 + 0):X2}";

                            ip += 1;
                            break;
                        }

                    // length is 2  
                    // either two registers as two operands
                    // or one 1-byte value 
                    case 2:
                        {
                            if (codeLine.FirstOperand == null) throw new AssemblerException($"Ожидается минимум один операнд, но было получено ноль: {textLine}");

                            // two registers 
                            if (codeLine.SecondOperand != null)
                            {
                                if (IsRegister(codeLine.FirstOperand) && IsRegister(codeLine.SecondOperand))
                                {
                                    // check for allocated memory overflow 
                                    OverflowCheck(ip + 2, textLine);

                                    // addressing type 00 
                                    binaryCodeLine = $"{"T"} {ip:X6} {command.Length:X2} {(command.Code * 4 + 0):X2}{GetRegisterNumber(codeLine.FirstOperand)}{GetRegisterNumber(codeLine.SecondOperand)}";

                                    ip += 2;
                                    break;
                                }
                                else
                                {
                                    throw new AssemblerException($"Неверный формат команды. Ожидалось два регистра: {textLine}");
                                }
                            }
                            // 1-byte value 
                            else
                            {
                                if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается один операнд, но найдено два: {textLine}");

                                int value;

                                // try convert 
                                try
                                {
                                    value = Convert.ToInt32(codeLine.FirstOperand, 10);
                                }
                                catch (Exception ex)
                                {
                                    throw new AssemblerException($"Невозможно преобразовать первый операнд в число: {textLine}");
                                }

                                // check if within 0-255
                                if (value < 0 || value > 255)
                                    throw new AssemblerException($"Значение первого операнда выходит за границы допустимого диапазона (0-255): {textLine}");

                                // check for allocated memory overflow 
                                OverflowCheck(ip + 2, textLine);

                                // addressing type 00 
                                binaryCodeLine = $"{"T"} {ip:X6} {command.Length:X2} {(command.Code * 4 + 0):X2}{value:X2}";

                                ip += 2;
                                break;
                            }
                        }

                    // length 4 
                    case 4:
                        {
                            if (codeLine.FirstOperand == null) throw new AssemblerException($"Ожидается один операнд, но было получено ноль: {textLine}");
                            if (codeLine.SecondOperand != null) throw new AssemblerException($"Ожидается один операнд, но найдено два: {textLine}");

                            // check for allocated memory overflow 
                            OverflowCheck(ip + 4, textLine);

                            // is it a label? 
                            if (IsLabel(codeLine.FirstOperand))
                            {
                                var symbolicName = GetSymbolicName(codeLine.FirstOperand);

                                if (symbolicName == null)
                                {
                                    TSI.Add(new SymbolicName()
                                    {
                                        Name = codeLine.FirstOperand.ToUpper(),
                                        AddressRequirements = [ip]
                                    });

                                    binaryCodeLine = $"{"T"} {ip:X6} {command.Length:X2}  {(command.Code * 4 + 1):X2}{(-1).ToString("X6").Substring(2)}";
                                }
                                else
                                {
                                    if (symbolicName.Address == -1) // undefined, push ip to AddressRequirements 
                                    {
                                        symbolicName.AddressRequirements.Add(ip);
                                        binaryCodeLine = $"{"T"} {ip:X6} {command.Length:X2}  {(command.Code * 4 + 1):X2}{(-1).ToString("X6").Substring(2):X6}";
                                    }
                                    else                            // defined 
                                    {
                                        binaryCodeLine = $"{"T"} {ip:X6} {command.Length:X2}  {(command.Code * 4 + 1):X2}{symbolicName.Address:X6}";
                                    }
                                }

                                ip += 4;
                                break;
                            }
                            // is it a parsable 3-byte value? 
                            else if (int.TryParse(codeLine.FirstOperand, out var value))
                            {
                                if (value < 0 || value > 16777215) throw new AssemblerException($"Недопустимое значение операнда: {textLine}");

                                // addressing type 01 
                                binaryCodeLine = $"{"T"} {ip:X6} {command.Length} {(command.Code * 4):X2}{value:X6}";

                                ip += 4;
                                break;
                            }
                            else
                            {
                                throw new AssemblerException($"Недопустимое значение операнда: {textLine}");
                            }
                        }
                }
            }
            else
                throw new AssemblerException($"Неизвестная команда: {textLine}"); 
                
            BinaryCode.Add(binaryCodeLine);

            lineIterator++;

            if(lineIterator >= SourceCode.Count())
            {
                lineIterator = -1; 
            }

            if (lineIterator == -1 && !endFlag) throw new AssemblerException($"Не найдена точка входа в программу.");

            return false; 
        }

        private void CheckAddressRequirements()
        {
            if (TSI.Any(x => x.AddressRequirements.Count() != 0))
                throw new AssemblerException($"Не всем меткам было присвоено значение");
        }

        private void ProvideAddresses(SymbolicName symbolicName)
        {
            foreach(var requirement in symbolicName.AddressRequirements)
            {
                var TLines = BinaryCode.Where(x => x.Split(' ')[0] != "H"); 

                var line = TLines.First(x => Convert.ToInt32(x.Split()[1], 16) == requirement);

                int index = BinaryCode.IndexOf(line);

                BinaryCode[index] = $"{line.Remove(line.Length - 6)}{symbolicName.Address:X6}";
            }

            symbolicName.AddressRequirements = new List<int>(); 
        }

        public void ClearTSI()
        {
            TSI.Clear();
        }

        public bool IsCommand(string? chunk)
        {
            if(chunk == null) return false;  

            return AvailibleCommands.Select(c => c.Name.ToUpper()).Contains(chunk.ToUpper()); 
        } 

        public bool IsDirective(string? chunk)
        {
            if (chunk == null) return false; 

            return AvailibleDirectives.Contains(chunk.ToUpper());
        }

        // is it a label-formatted chunk and is it distinct from commands & directives 
        public bool IsLabel(string? chunk)
        {
            if(chunk == null) return false;

            if (chunk.Length > 10) return false; 

            if (!"qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM".Contains(chunk[0])) return false; 

            if (!chunk.All(c => "1234567890qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM_".Contains(c))) return false;

            if (IsRegister(chunk.ToUpper())) return false; 

            if (AvailibleCommands.Select(c => c.Name.ToUpper()).Contains(chunk.ToUpper())
                || AvailibleDirectives.Select(c => c.ToUpper()).Contains(chunk.ToUpper())) return false;

            return true; 
        }

        public static bool IsXString(string? chunk)
        {
            if (chunk == null
                || !chunk.StartsWith("X\"", StringComparison.OrdinalIgnoreCase)
                || !chunk.EndsWith('\"'))
                return false;

            string symbols = chunk.Trim('X').Trim('\"').ToUpper();

            if (symbols.Length < 1
                || symbols.Contains('\"')
                || !symbols.All(c => "01234567890ABCDEF".Contains(c))
                || symbols.Length % 2 != 0
                )
                return false;

            return true;
        }

        public static bool IsCString(string? chunk)
        {
            if (chunk == null
                || !chunk.StartsWith("C\"", StringComparison.OrdinalIgnoreCase)
                || !chunk.EndsWith('\"')
                || chunk.Length < 4)
                return false;

            string symbols = chunk.Substring(1, chunk.Length-1);

            if (symbols.Length < 1
                || symbols.Any(c => c > 127) 
                )
                return false;

            return true; 
        }

        public static bool IsRegister(string? chunk)
        {
            if (chunk == null) return false; 

            return Regex.IsMatch(chunk, @"^R(?:[1-9]|1[0-6])$");
        }

        public static int GetRegisterNumber(string chunk)
        {
            return int.Parse(chunk.Substring(1)) - 1;
        }
 
        public SymbolicName? GetSymbolicName(string chunk)
        {
            var symbolicName = TSI.Find(n => n.Name.ToUpper() == chunk.ToUpper());

            return symbolicName; 
        }
 
        public static string? ConvertToASCII(string chunk)
        {
            string result = "";
            byte[] textBytes = Encoding.ASCII.GetBytes(chunk);
            for (int i = 0; i < textBytes.Length; i++)
            {
                result = result + textBytes[i].ToString("X2");
            }
            return result;
        }

        public static void OverflowCheck(int value, string textLine)
        {
            if (value < 0 || value > maxAddress) throw new AssemblerException($"Произошло переполнение выделенной памяти: {textLine}");
        }

        // returns a command object that has nullable parameters (label, first operand and second operand) and a non-nullable command. 
        // guarantees that label & command/directive fit the formet. doesnt check operands 
        // labels & commands/directives are set to upper case 
        public CodeLine GetCodeLineFromSource(List<string> line)
        {
            var textLine = string.Join(" ", line);

            if(line.Count < 1 || line.Count > 4)
                throw new AssemblerException($"Неверный формат команды: {textLine}");

            switch (line.Count) 
            {
                case 1:
                    // can only be an operand-less command or END 
                    if (IsCommand(line[0]) || line[0].ToUpper() == "END")
                    {
                        return new CodeLine()
                        {
                            Label = null,
                            Command = line[0].ToUpper(), 
                            FirstOperand = null, 
                            SecondOperand = null 
                        };  
                    } 
                    else
                    {
                        //throw new AssemblerException($"Неверный формат команды. Ожидается команда без операндов или директива END без операнда: {textLine}");
                        throw new AssemblerException($"Неверный формат команды: {textLine}");
                    }

                case 2:
                    // can be a label and an operand-less command or start/end 
                    if (IsLabel(line[0]) && (IsCommand(line[1]) || line[1].ToUpper() == "START" || line[1].ToUpper() == "END"))
                    {
                        return new CodeLine()
                        {
                            Label = line[0].ToUpper(),
                            Command = line[1].ToUpper(),
                            FirstOperand = null,
                            SecondOperand = null
                        };
                    }
                    // can be a command with one operand
                    // or a keyword with one operand
                    else if (IsCommand(line[0]) || IsDirective(line[0]))
                    {
                        return new CodeLine()
                        {
                            Label = null, 
                            Command = line[0].ToUpper(),
                            FirstOperand = line[1], 
                            SecondOperand = null
                        };
                    }
                    else
                    {
                        //throw new AssemblerException($"Неверный формат команды. Ожидается метка с командой без операндов либо команда/директива с одним операндом: {textLine}");
                        throw new AssemblerException($"Неверный формат команды: {textLine}");
                    }

                case 3:
                    // can be a label and a keyword with one operand
                    // can be a command with two operands 
                    if (IsLabel(line[0]) &&
                        (IsCommand(line[1]) || IsDirective(line[1])))
                    {
                        return new CodeLine()
                        {
                            Label = line[0].ToUpper(),
                            Command = line[1].ToUpper(),
                            FirstOperand = line[2], 
                            SecondOperand = null
                        };
                    }
                    else if (IsCommand(line[0]))
                    {
                        return new CodeLine()
                        {
                            Label = null,
                            Command = line[0].ToUpper(),
                            FirstOperand = line[1], 
                            SecondOperand = line[2] 
                        };
                    }
                    else
                    {
                        //throw new AssemblerException($"Неверный формат команды. Ожидается метка и команда/директива с одним операндом либо команда с двумя операндами: {textLine}");
                        throw new AssemblerException($"Неверный формат команды: {textLine}");
                    }

                case 4:
                    // can only be a label and a command and two operands 
                    if (IsLabel(line[0])
                        && IsCommand(line[1]))
                    {
                        return new CodeLine()
                        {
                            Label = line[0].ToUpper(),
                            Command = line[1].ToUpper(),
                            FirstOperand = line[2], 
                            SecondOperand = line[3] 
                        };
                    }
                    else
                    {
                        //throw new AssemblerException($"Неверный формат команды. Ожидается метка и команда с двумя операндами: {textLine}");
                        throw new AssemblerException($"Неверный формат команды: {textLine}"); 
                    }

                default:
                    throw new AssemblerException($"Неверный формат команды. Ни один из известных форматов не применим: {textLine}");
            }
        }
    }
}
