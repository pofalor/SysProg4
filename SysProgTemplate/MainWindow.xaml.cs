using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SysProgTemplate.Components;
using SysProgTemplateShared.Structure;
using SysProgTemplateShared;
using System.Text.Json;
using SysProgTemplateShared.Exceptions;
using SysProgTemplateShared.Dto;
using SysProgTemplateShared.Helpers;
using System.Runtime.InteropServices.Swift;


namespace SysProgTemplate
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Assembler Assembler {get; set; } = new Assembler();

        // Исходный код
        private string SourceCode { get; set; } =
                        @"PROG  START   1
    JMP   M2 
M1  WORD  40 
M2  BYTE  C""!""
M3  BYTE  12	
    SUB  R1 R2
    ADD  R3 R4
    CALL  M1
    INT   200	
    END 
            ";

        private TextBox SourceCodeTextBox { get; set; }

        // Таблица кодов операций 
        private TextBox CommandsTextBox { get; set; }

        //Двоичный код 
        private TextBox BinaryCodeTextBox { get; set; }

        // ТСИ 
        private TextBox TSITextBox { get; set; }

        // Ошибки 
        private TextBox ErrorsTextBox { get; set; } 

        // Кнопка
        private Button ProcessStepButton { get; set; } 
        
        private Button PassButton { get; set; } 


        public MainWindow()
        {
            InitializeComponent();

            SourceCodeTextBox = this.SourceCode_TextBox;
            SourceCodeTextBox.Text = SourceCode;

            CommandsTextBox = this.Commands_TextBox;
            CommandsTextBox.Text = string.Join("\n", Assembler.AvailibleCommands.Select(c => $"{c.Name} {c.Code} {c.Length}")); 

            BinaryCodeTextBox = this.BinaryCode_TextBox; 

            TSITextBox = this.TSI_TextBox;

            ErrorsTextBox = this.Errors_TextBox; 

            ProcessStepButton = this.ProcessStep_Button;
            PassButton = this.Pass_Button;

            Reset();
        }
        int num_step = 0;
        private void ProcessStep_Button_Click(object sender, RoutedEventArgs e)
        {
            if (num_step == 0)
            { Reset(); }
            try
            {
                Assembler.ProcessStep();
                num_step = 1;
                BinaryCodeTextBox.Text = string.Join("\n", Assembler.BinaryCode);
                TSITextBox.Text = string.Join("\n", Assembler.TSI.Select(w => $"{w.Name} {w.Address.ToString("X6")} {string.Join(' ', w.AddressRequirements.Select(x => x.ToString("X6")))}")); 
            }
            catch (AssemblerException ex)
            {
                ErrorsTextBox.Text = $"Ошибка: {ex.Message}"; 
            }

            if (!string.IsNullOrEmpty(ErrorsTextBox.Text))
            {
                ProcessStepButton.IsEnabled = false; 
            }
        }

        private void Reset_Button_Click(object sender, RoutedEventArgs e)
        {
            Reset(); 
        }

        public void Reset()
        {
            if (ProcessStepButton == null || TSITextBox == null || BinaryCodeTextBox == null || ErrorsTextBox == null) return; 

            try
            {
                ProcessStepButton.IsEnabled = true;
                PassButton.IsEnabled = true;
                TSITextBox.Text = null;
                BinaryCodeTextBox.Text = null;
                ErrorsTextBox.Text = null;

                var newCommands = Parser.TextToCommandDtos(CommandsTextBox.Text);
                var sourceCode = Parser.ParseCode(SourceCodeTextBox.Text);

                Assembler.Reset(sourceCode, newCommands);
            }
            catch (AssemblerException ex)
            {
                ErrorsTextBox.Text = $"Ошибка: {ex.Message}";
            }

            if (!string.IsNullOrEmpty(ErrorsTextBox.Text))
            {
                ProcessStepButton.IsEnabled = false;
                PassButton.IsEnabled = false;
            }
        }

        private void SourceCode_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var newSourceCode = Parser.ParseCode(SourceCodeTextBox.Text);

            if (!Comparer.CompareSourceCodeVersions(Assembler.SourceCode, newSourceCode))
            {
                Reset();
            }
        }

        private void Commands_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Reset();
        }

        private void Pass_Button_Click(object sender, RoutedEventArgs e)
        {
            Reset();
            while (true) { 
                try
                {
                    var hasFinished = Assembler.ProcessStep();

                    BinaryCodeTextBox.Text = string.Join("\n", Assembler.BinaryCode);
                    TSITextBox.Text = string.Join("\n", Assembler.TSI.Select(w => $"{w.Name} {w.Address.ToString("X6")} {string.Join(' ', w.AddressRequirements.Select(x => x.ToString("X6")))}")); 

                    if (hasFinished)
                    {
                        break; 
                    }
                    num_step = 0;
                }
                catch (AssemblerException ex)
                {
                    ErrorsTextBox.Text = $"Ошибка: {ex.Message}";
                    break; 
                }

                if (!string.IsNullOrEmpty(ErrorsTextBox.Text))
                {
                    ProcessStepButton.IsEnabled = false;
                    break; 
                }
            }
        }
    }
}
