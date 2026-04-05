using rofs_extract;

Console.WriteLine("3DS ROFS Dumper");
Console.WriteLine("Supports V1 (≤0.10.2) and V2 (0.12.0) formats");
Console.WriteLine("----------------------------------------------");

if (args.Length < 2)
{
    Console.WriteLine("Usage: rofs_extract <infile> <outfolder>");
    return 1;
}

try
{
    RofsFile rofs = RofsFile.Load(args[0]);
    rofs.ExtractTo(args[1]);
    return 0;
}
catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}
