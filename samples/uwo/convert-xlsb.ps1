# Saves every .xlsb in this folder as .xlsx, into ./converted.
#
# Why this exists: uwo's live workbooks are .xlsb, and the table boundaries in that
# layout are the workbook's defined names. NPOI - which SheetMan reads workbooks with -
# does not open .xlsb at all, and the readers that do (ExcelDataReader and friends) hand
# back cells without the defined names, so they cannot find a table. Converting keeps the
# converter itself cross-platform and NPOI-only.
#
# The conversion is lossless for what matters here: every workbook-scoped defined name
# survives pointing at the same range. uwo's own exporter reads .xlsb through ACE OLEDB
# and warns in its source that the path truncates long text at 255 characters, so an
# .xlsx is the safer file to read either way.
#
# Output is gitignored: the .xlsx is about twice the size of the .xlsb and it is derived,
# not authored. Run this once on a machine with Excel installed.
#
#     powershell -File samples/uwo/convert-xlsb.ps1
#
# LibreOffice does the same job without Excel:
#     soffice --headless --convert-to xlsx --outdir converted *.xlsb

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $here 'converted'
New-Item -ItemType Directory -Force $outDir | Out-Null

$files = Get-ChildItem "$here\*.xlsb" -File
if ($files.Count -eq 0) {
  Write-Host "No .xlsb here."
  exit 0
}

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$xl.AskToUpdateLinks = $false
$xl.EnableEvents = $false
$xl.ScreenUpdating = $false

$converted = 0
try {
  foreach ($f in $files) {
    $target = Join-Path $outDir ([System.IO.Path]::GetFileNameWithoutExtension($f.Name) + '.xlsx')
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    try {
      $wb = $xl.Workbooks.Open($f.FullName, 0, $true)

      # 51 = xlOpenXMLWorkbook. Macros are not carried across; these workbooks are data.
      $wb.SaveAs($target, 51)
      $names = $wb.Names.Count
      $wb.Close($false)

      $converted++
      Write-Host ("{0,-34} {1,7:N1} MB -> {2,7:N1} MB  names={3}  {4:0.0}s" -f `
        $f.Name, ($f.Length / 1MB), ((Get-Item $target).Length / 1MB), $names, $sw.Elapsed.TotalSeconds)
    }
    catch {
      Write-Host ("{0,-34} FAILED: {1}" -f $f.Name, $_.Exception.Message)
    }
  }
}
finally {
  try { $xl.Quit() } catch {}
  [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
}

Write-Host ""
Write-Host "$converted of $($files.Count) converted into $outDir"
