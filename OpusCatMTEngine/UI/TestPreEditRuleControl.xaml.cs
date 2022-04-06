﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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

namespace OpusCatMTEngine
{
    /// <summary>
    /// Interaction logic for TestPreEditRuleControl.xaml
    /// </summary>
    public partial class TestPreEditRuleControl : UserControl
    {
        public static readonly DependencyProperty RuleCollectionProperty = DependencyProperty.Register(
            "RuleCollection", typeof(AutoEditRuleCollection),
            typeof(TestPreEditRuleControl)
            );

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            "Title", typeof(string),
            typeof(TestPreEditRuleControl)
            );

        public bool TestActive { get; private set; }
        public AutoEditRuleCollection RuleCollection
        {
            get => (AutoEditRuleCollection)GetValue(RuleCollectionProperty);
            set => SetValue(RuleCollectionProperty, value);
        }

        public TextBox PreEditPattern { get; private set; }
        public TextBox PreEditReplacement { get; private set; }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty,value);
        }

        //Two constructors: one for testing a single rule, other for testing whole collection
        public TestPreEditRuleControl(TextBox PreEditPattern, TextBox PreEditReplacement)
        {
            this.PreEditPattern = PreEditPattern;
            this.PreEditReplacement = PreEditReplacement;
            InitializeComponent();
        }

        public TestPreEditRuleControl(AutoEditRuleCollection ruleCollection)
        {
            this.RuleCollection = ruleCollection;
            InitializeComponent();
        }

        public TestPreEditRuleControl()
        {
            this.DataContext = this;
            InitializeComponent();
        }

        private void PreEditTest_Click(object sender, RoutedEventArgs e)
        {
            //If these have been defined, generate rule collection from them
            if (this.PreEditPattern != null && this.PreEditReplacement != null)
            {
                this.RuleCollection = new AutoEditRuleCollection();
                this.RuleCollection.AddRule(
                    new AutoEditRule()
                    {
                        SourcePattern = this.PreEditPattern.Text,
                        Replacement = this.PreEditReplacement.Text
                    });
            }

            TextRange textRange = new TextRange(this.SourceBox.Document.ContentStart, this.SourceBox.Document.ContentEnd);
            var sourceText = textRange.Text.TrimEnd('\r', '\n');
            try
            {
                var result = this.RuleCollection.ProcessPreEditRules(sourceText);
                this.PopulateSourceBox(result);
                this.PopulateTargetBox(result);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show($"Error in regular expression: {ex.Message}");
            }

            this.TestActive = true;

        }

        private void PopulateSourceBox(AutoEditResult result)
        {
            //Store the source text, use it as basis of the source text with match highlights
            TextRange textRange = new TextRange(this.SourceBox.Document.ContentStart, this.SourceBox.Document.ContentEnd);
            var sourceText = textRange.Text.TrimEnd('\r', '\n'); ;

            int nonMatchStartIndex = 0;
            Paragraph matchHighlightSource = new Paragraph();
            foreach (var replacement in result.AppliedReplacements)
            {

                if (nonMatchStartIndex < replacement.Match.Index)
                {
                    var nonMatchText =
                        sourceText.Substring(
                            nonMatchStartIndex, replacement.Match.Index - nonMatchStartIndex);
                    matchHighlightSource.Inlines.Add(nonMatchText);
                }

                var matchText = replacement.Match.Value;
                var matchRun = new Run(matchText)
                { Background = Brushes.Chartreuse, ToolTip = replacement.Rule.SourcePattern };

                matchHighlightSource.Inlines.Add(matchRun);

                nonMatchStartIndex = replacement.Match.Index + replacement.Match.Length;
            }

            if (nonMatchStartIndex < sourceText.Length)
            {
                var nonMatchText =
                        sourceText.Substring(
                            nonMatchStartIndex);
                matchHighlightSource.Inlines.Add(nonMatchText);
            }

            this.SourceBox.Document.Blocks.Clear();
            this.SourceBox.Document.Blocks.Add(matchHighlightSource);
        }

        private void PopulateTargetBox(AutoEditResult result)
        {
            var editedSourceText = result.Result;

            int nonMatchStartIndex = 0;
            Paragraph matchHighlightSource = new Paragraph();
            foreach (var replacement in result.AppliedReplacements)
            {

                if (nonMatchStartIndex < replacement.OutputIndex)
                {
                    var nonMatchText =
                        editedSourceText.Substring(
                            nonMatchStartIndex, replacement.OutputIndex - nonMatchStartIndex);
                    matchHighlightSource.Inlines.Add(nonMatchText);
                }

                var matchText = replacement.Output;
                var matchRun = new Run(matchText)
                { Background = Brushes.Chartreuse, ToolTip = replacement.Rule.Replacement };

                matchHighlightSource.Inlines.Add(matchRun);

                nonMatchStartIndex = replacement.OutputIndex + replacement.OutputLength;
            }

            if (nonMatchStartIndex < editedSourceText.Length)
            {
                var nonMatchText =
                        editedSourceText.Substring(
                            nonMatchStartIndex);
                matchHighlightSource.Inlines.Add(nonMatchText);
            }

            this.EditedSourceBox.Document.Blocks.Clear();
            this.EditedSourceBox.Document.Blocks.Add(matchHighlightSource);
        }

        private void AnyControl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.TestActive)
            {
                //Keep this on top, otherwise endless loop
                this.TestActive = false;

                //Clear edited output
                this.EditedSourceBox.Document.Blocks.Clear();

                //Remove highlights from source and unedited output
                TextRange sourceTextRange = new TextRange(this.SourceBox.Document.ContentStart, this.SourceBox.Document.ContentEnd);
                var sourceText = sourceTextRange.Text.TrimEnd('\r', '\n');
                this.SourceBox.Document.Blocks.Clear();
                this.SourceBox.Document.Blocks.Add(new Paragraph(new Run(sourceText)));
            }
        }
    }
}
