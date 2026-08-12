using ABPLCReaderWriter.Models;
using ABPLCReaderWriter.Services;
using Spectre.Console;

namespace ABPLCReaderWriter;

internal class Program
{
    private static readonly NetworkScanner Scanner = new();
    private static readonly PlcTagService TagService = new();

    private static List<PlcDevice> _discovered = new();
    private static PlcDevice? _selectedDevice;
    private static Dictionary<string, List<PlcTagInfo>> _groupedTags = new();

    static async Task Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("AB PLC Reader").Color(Color.Blue));
        AnsiConsole.MarkupLine("[grey]Allen-Bradley ControlLogix / CompactLogix tag browser[/]");
        AnsiConsole.MarkupLine("[grey]Uses libplctag • Future write support planned[/]\n");

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Main Menu[/]")
                    .PageSize(10)
                    .AddChoices(
                        "1. Scan network for PLCs",
                        "2. Manually add PLC by IP",
                        "3. Select PLC from list",
                        "4. List / browse tags (grouped)",
                        "5. Read attributes of a selected tag group",
                        "6. Exit"));

            try
            {
                switch (choice[0])
                {
                    case '1':
                        await ScanNetworkAsync();
                        break;
                    case '2':
                        await ManualAddPlcAsync();
                        break;
                    case '3':
                        SelectPlc();
                        break;
                    case '4':
                        await BrowseTagsAsync();
                        break;
                    case '5':
                        await ReadSelectedTagAttributesAsync();
                        break;
                    case '6':
                        AnsiConsole.MarkupLine("[green]Goodbye.[/]");
                        return;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                AnsiConsole.MarkupLine("[red]Press any key to continue...[/]");
                Console.ReadKey(true);
            }

            AnsiConsole.WriteLine();
        }
    }

    private static async Task ScanNetworkAsync()
    {
        var range = AnsiConsole.Ask<string>("Enter IP range or CIDR (e.g. [cyan]192.168.1.0/24[/] or [cyan]192.168.1.10-50[/]):");

        var progress = new Progress<string>(msg => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]"));

        await AnsiConsole.Status()
            .StartAsync("Scanning network...", async ctx =>
            {
                _discovered = await Scanner.ScanAsync(range, progress);
            });

        if (_discovered.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No EtherNet/IP devices found on the given range.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]Found {_discovered.Count} candidate device(s):[/]");
        foreach (var d in _discovered)
            AnsiConsole.MarkupLine($"  • {d.IpAddress}");

        if (AnsiConsole.Confirm("Attempt to identify Logix PLCs by querying @tags?", false))
        {
            foreach (var d in _discovered.ToList())
            {
                try
                {
                    var tags = await TagService.ListTagsGroupedAsync(d, includeProgramTags: false, timeout: TimeSpan.FromSeconds(3));
                    d.Name = tags.Count > 0 ? $"Logix ({tags.Count} roots)" : "Logix (empty?)";
                }
                catch
                {
                    d.Name = "EIP device (not Logix or unreachable)";
                }
            }
        }
    }

    private static async Task ManualAddPlcAsync()
    {
        var ip = AnsiConsole.Ask<string>("PLC IP address:");
        var path = AnsiConsole.Ask("CIP path (usually 1,0 for backplane + slot 0):", "1,0");
        var name = AnsiConsole.Ask("Friendly name (optional):", "");

        var device = new PlcDevice { IpAddress = ip, Path = path, Name = name };
        _discovered.Add(device);
        _selectedDevice = device;
        AnsiConsole.MarkupLine($"[green]Added {device.DisplayName}[/]");
        await Task.CompletedTask;
    }

    private static void SelectPlc()
    {
        if (_discovered.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No devices in list. Scan or add manually first.[/]");
            return;
        }

        _selectedDevice = AnsiConsole.Prompt(
            new SelectionPrompt<PlcDevice>()
                .Title("Select PLC:")
                .PageSize(15)
                .UseConverter(d => d.DisplayName)
                .AddChoices(_discovered));

        AnsiConsole.MarkupLine($"[green]Selected: {_selectedDevice.DisplayName}[/]");
    }

    private static async Task BrowseTagsAsync()
    {
        if (_selectedDevice == null)
        {
            AnsiConsole.MarkupLine("[yellow]Select a PLC first.[/]");
            return;
        }

        await AnsiConsole.Status()
            .StartAsync($"Reading tag list from {_selectedDevice.IpAddress}...", async ctx =>
            {
                _groupedTags = await TagService.ListTagsGroupedAsync(_selectedDevice, includeProgramTags: true);
            });

        if (_groupedTags.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tags returned. Check path, firewall, or PLC type.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]{_groupedTags.Count} tag groups found.[/]");

        var root = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a tag group (root name):")
                .PageSize(20)
                .MoreChoicesText("[grey](Move up/down to see more)[/]")
                .AddChoices(_groupedTags.Keys.OrderBy(k => k)));

        var members = _groupedTags[root];
        AnsiConsole.MarkupLine($"\n[cyan]Members of '{root}':[/]");
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Full Name");
        table.AddColumn("Type");
        table.AddColumn("Length");
        table.AddColumn("Dimensions");

        foreach (var m in members)
        {
            table.AddRow(
                m.Name,
                m.TypeDescription,
                m.Length.ToString(),
                string.Join(",", m.Dimensions.Where(d => d > 0)));
        }
        AnsiConsole.Write(table);
    }

    private static async Task ReadSelectedTagAttributesAsync()
    {
        if (_selectedDevice == null || _groupedTags.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Select a PLC and browse tags first.[/]");
            return;
        }

        var root = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select tag group to read all attributes/values:")
                .PageSize(20)
                .AddChoices(_groupedTags.Keys.OrderBy(k => k)));

        var members = _groupedTags[root];

        Dictionary<string, string> values = new Dictionary<string, string>();
        await AnsiConsole.Status()
            .StartAsync($"Reading {members.Count} member(s)...", async ctx =>
            {
                values = await TagService.ReadTagAttributesAsync(_selectedDevice, root, members);
            });

        // Console table
        var table = new Table().Border(TableBorder.Rounded).Title($"[green]{root}[/]");
        table.AddColumn("Tag.Attribute");
        table.AddColumn("Data Type");
        table.AddColumn("Value");

        // CSV: A = tag.attribute, B = data type, C = value
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Tag.Attribute,Data Type,Value");

        foreach (var member in members.OrderBy(m => m.Name))
        {
            string val = "";
            if (values.ContainsKey(member.Name))
                val = values[member.Name];

            string csvTag = EscapeCsv(member.Name);
            string csvType = EscapeCsv(member.TypeDescription);
            string csvValue = EscapeCsv(val);

            csv.AppendLine(string.Format("{0},{1},{2}", csvTag, csvType, csvValue));

            table.AddRow(member.Name, member.TypeDescription, Markup.Escape(val));
        }

        AnsiConsole.Write(table);

        string safeRoot = string.Join("_", root.Split(System.IO.Path.GetInvalidFileNameChars()));
        string fileName = string.Format("tag_{0}_{1:yyyyMMdd_HHmmss}.csv", safeRoot, DateTime.Now);
        string fullPath = System.IO.Path.Combine(Environment.CurrentDirectory, fileName);

        System.IO.File.WriteAllText(fullPath, csv.ToString());
        AnsiConsole.MarkupLine($"\n[green]CSV written to:[/] [cyan]{fullPath}[/]");
        AnsiConsole.MarkupLine("[grey]Columns: A=Tag.Attribute  B=Data Type  C=Value[/]");
    }

    private static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";

        bool needsQuotes = field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r");
        if (!needsQuotes)
            return field;

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
