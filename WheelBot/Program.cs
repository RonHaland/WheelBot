using WheelBot;

var generator = new WheelGenerator();
generator.AddOption("CHug");
generator.AddOption("Chris");
generator.AddOption("Del ut 2");
generator.AddOption("Gutta drikker");
generator.AddOption("Drikk 1");
generator.RandomizeOrder();
var result = await generator.GenerateAnimations();

Console.WriteLine(result);