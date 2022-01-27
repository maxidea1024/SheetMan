using CommandLine;
using SheetMan.Recipe;
using SheetMan.Models;

namespace SheetMan.CodeGeneration
{
    public class CppCodeGenerator
    {
        private Options _options;
        private RecipeModel _recipeModel;
        private Model _model;

        public void Generate(Options options, RecipeModel recipeModel, Model model)
        {
            _options = options;
            _recipeModel = recipeModel;
            _model = model;


        }
    }
}
