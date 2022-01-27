using System.Collections.Generic;
using CommandLine;

namespace SheetMan
{
    public class Options
    {
        [Option('r', "recipe", HelpText = "Recipe file.")]
        public string RecipeFilename { get; set; }

        //todo 이게 실제 의미가 없네...
        //빈 템플릿 오브젝트를 의미있게 만들어줘야함.
        [Option("new-recipe", HelpText = "Create empty recipe file.")]
        public string NewRecipeFilename { get; set; }

        [Option("verbose", HelpText = "Sets whether to output debugging log messages.")]
        public bool Verbose { get; set; }

        [Option("silent", HelpText = "Suppress all logging message except ERROR/FATAL.")]
        public bool Silent { get; set; }
        
        [Option("debug", HelpText = "Enables or disables internal debugging.")]
        public bool Debugging { get; set; }
    }
}
