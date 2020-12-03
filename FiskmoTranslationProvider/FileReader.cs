﻿

using Sdl.Core.Globalization;
using Sdl.FileTypeSupport.Framework.BilingualApi;
using Sdl.LanguagePlatform.TranslationMemory;
using Sdl.LanguagePlatform.TranslationMemoryApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FiskmoTranslationProvider
{
    public class FileReader : AbstractBilingualContentProcessor
    {
        internal List<Tuple<string,string>> FileTranslations;
        internal List<string> FileNewSegments;
        private FinetuneBatchTaskSettings settings;
        private FiskmoMarkupDataVisitor sourceVisitor;
        private FiskmoMarkupDataVisitor targetVisitor;

        public FileReader(FinetuneBatchTaskSettings settings)
        {
            this.settings = settings;
            this.FileTranslations = new List<Tuple<string, string>>();
            this.FileNewSegments = new List<string>();
            this.sourceVisitor = new FiskmoMarkupDataVisitor();
            this.targetVisitor = new FiskmoMarkupDataVisitor();
        }
        
        public override void ProcessParagraphUnit(IParagraphUnit paragraphUnit)
        {
      
            // Check if this paragraph actually contains segments 
            // If not, it is just a structure tag content, which is not processed
            if (paragraphUnit.IsStructure)
            {
                return;
            }
            
            foreach (ISegmentPair segmentPair in paragraphUnit.SegmentPairs)
            {
                if (segmentPair.Properties.ConfirmationLevel == ConfirmationLevel.Translated ||
                    segmentPair.Properties.ConfirmationLevel == ConfirmationLevel.ApprovedTranslation ||
                    segmentPair.Properties.ConfirmationLevel == ConfirmationLevel.ApprovedSignOff)
                {
                    this.sourceVisitor.Reset();
                    segmentPair.Source.AcceptVisitor(this.sourceVisitor);
                    this.targetVisitor.Reset(this.sourceVisitor.TagStarts);
                    segmentPair.Target.AcceptVisitor(this.targetVisitor);

                    FileTranslations.Add(new Tuple<string, string>(
                        this.sourceVisitor.PlainText,
                        this.targetVisitor.PlainText));
                }
                else
                {
                    this.sourceVisitor.Reset();
                    segmentPair.Source.AcceptVisitor(this.sourceVisitor);
                    //If segment does not have translation, add it to new strings and look for fuzzies
                    FileNewSegments.Add(this.sourceVisitor.PlainText);
                }
            }
        }
    }
}