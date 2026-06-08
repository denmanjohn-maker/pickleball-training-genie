using System;
class Program {
  static void Main() {
    var connectionString = "postgresql://postgres:YYMMJgYVMqjwjthmZzawmtGxqzGMgGMS@postgres.railway.internal:5432/railway";
    if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
    {
        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':');
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.LocalPath.TrimStart('/')};Username={userInfo[0]};Password={password};SslMode=Require;TrustServerCertificate=True;";
        Console.WriteLine(connectionString);
    } else {
        Console.WriteLine("Did not match scheme");
    }
  }
}
