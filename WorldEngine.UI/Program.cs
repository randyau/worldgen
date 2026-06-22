using WorldEngine.UI;

try
{
    using var game = new Game1();
    game.Run();
}
catch (Exception ex)
{
    var log = Path.Combine(AppContext.BaseDirectory, "crash.log");
    File.WriteAllText(log, $"{DateTime.Now:u}\n{ex}");
    throw;
}
