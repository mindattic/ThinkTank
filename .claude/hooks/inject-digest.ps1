<#
  SessionStart hook: inject docs/BIBLE.digest.md as authoritative context.
  Emits Claude Code hook JSON on stdout. Windows PowerShell 5.1 / Win-1252 safe:
  all non-ASCII is escaped to \uXXXX so the JSON survives a non-UTF8 console.
  If the digest is missing/empty, emits {}.
#>
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$digestPath = Join-Path $repoRoot 'docs\BIBLE.digest.md'

function Write-Empty { Write-Output '{}'; exit 0 }

if (-not (Test-Path $digestPath)) { Write-Empty }
$digest = [IO.File]::ReadAllText($digestPath)
if ([string]::IsNullOrWhiteSpace($digest)) { Write-Empty }

$preamble = @"
The following is the AUTHORITATIVE Codex digest for the Think Tank (TT) project, generated from
docs/BIBLE.md. Treat it as the source of truth for what the project IS, is NOT, and its Laws.
Reference bible sections by their stable {#TT-...} anchors, never by line number. Full detail and
the user stories live in docs/BIBLE.md, docs/USER_STORIES.md, and docs/AMENDMENTS.md.

"@

$context = $preamble + $digest

# JSON-encode with ASCII-safe \uXXXX escaping for every non-ASCII char.
function ConvertTo-JsonString([string]$s) {
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.Append('"')
  foreach ($ch in $s.ToCharArray()) {
    $code = [int][char]$ch
    switch ($ch) {
      '"'  { [void]$sb.Append('\"');  continue }
      '\'  { [void]$sb.Append('\\');  continue }
      "`b" { [void]$sb.Append('\b');  continue }
      "`f" { [void]$sb.Append('\f');  continue }
      "`n" { [void]$sb.Append('\n');  continue }
      "`r" { [void]$sb.Append('\r');  continue }
      "`t" { [void]$sb.Append('\t');  continue }
      default {
        if ($code -lt 32 -or $code -gt 126) { [void]$sb.Append('\u{0:x4}' -f $code) }
        else { [void]$sb.Append($ch) }
      }
    }
  }
  [void]$sb.Append('"')
  return $sb.ToString()
}

$ctxJson = ConvertTo-JsonString $context
$out = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":' + $ctxJson + '}}'
Write-Output $out
