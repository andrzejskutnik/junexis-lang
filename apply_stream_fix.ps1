param()
$ErrorActionPreference = 'Stop'
$file = 'compiler\JunexisLang.Compiler.CodeGen\GoCodeGenerator.cs'
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
$list = [System.Collections.Generic.List[string]]::new($lines)

# Replace lines 244-258 (0-indexed 243-257) with Stream-aware version
# Line 244: "                     else"
# Line 258: "                     }"
Write-Host "Before: $($list[255])"  # should be make(chan ...)

# Remove lines 243..257 (15 lines: else { ... })
$list.RemoveRange(243, 15)

$replacement = [string[]]@(
'                    else',
'                    {',
'                        // Cross-platform source: create placeholder channel or Stream',
'                        wireVar = $"__wire{wireIdx++}";',
'                        var srcTask2 = ir.Tasks.FirstOrDefault(t => t.Name == use.SourceTask);',
'                        var elemType2b = srcTask2?.OutputType switch',
'                        {',
'                            IRCommType { Element: var el } => TypeMappings.ToGo(el),',
'                            IRType t2b => TypeMappings.ToGo(t2b),',
'                            _ => "interface{}"',
'                        };',
'                        // Check if the target param expects *Stream[T]',
'                        var tgtTask2 = tasks.FirstOrDefault(t => t.Name == use.TargetTask);',
'                        var argIndex2 = wireArgs[use.TargetTask].Count;',
'                        var tgtParam2 = argIndex2 < (tgtTask2?.Params.Count ?? 0) ? tgtTask2!.Params[argIndex2] : null;',
'                        if (tgtParam2?.Type is IRCommType { Kind: IRCommKind.Stream })',
'                        {',
'                            sb.AppendLine($"\t// {use.SourceTask} runs on another platform -- placeholder Stream");',
'                            sb.AppendLine($"\t{wireVar} := NewStream[{elemType2b}]()");',
'                        }',
'                        else',
'                        {',
'                            sb.AppendLine($"\t// {use.SourceTask} runs on another platform -- placeholder channel");',
'                            sb.AppendLine($"\t{wireVar} := make(chan {elemType2b})");',
'                        }',
'                        taskOutputWire[use.SourceTask] = wireVar;',
'                    }'
)
$list.InsertRange(243, $replacement)
Write-Host "After replacement. Lines: $($list.Count)"

$allText = $list -join "`r`n"
$outBytes = [System.Text.Encoding]::UTF8.GetBytes($allText)
[System.IO.File]::WriteAllBytes($file, $outBytes)
Write-Host "Done."
