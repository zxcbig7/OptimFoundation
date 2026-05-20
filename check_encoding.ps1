$f = 'c:\Users\zxcbi\Desktop\OptimizationFramework\OptimFoundation\Templates\Template_CPLEX\Constraints\Constraint_DoubleOffLT2.cs'
$b = [System.IO.File]::ReadAllBytes($f)

# Find "// Flag" position via ASCII scan
$flagPos = -1
for ($i = 0; $i -lt $b.Length - 7; $i++) {
    if ($b[$i] -eq 0x2F -and $b[$i+1] -eq 0x2F -and $b[$i+2] -eq 0x20 -and $b[$i+3] -eq 0x46) {
        $flagPos = $i; break
    }
}

Write-Host "// Flag found at byte position: $flagPos"
if ($flagPos -ge 0) {
    $hex = $b[$flagPos..($flagPos+15)] | ForEach-Object { $_.ToString('X2') }
    Write-Host ("Hex: " + ($hex -join ' '))
}

# Try UTF-8 decode
$utf8 = [System.Text.Encoding]::UTF8
$utf8text = $utf8.GetString($b)
$lines = $utf8text -split "`n"
$garbledLines = $lines | Where-Object { $_ -match '\?' } | Select-Object -First 5
Write-Host "`nLines with ? (UTF-8 decode):"
$garbledLines | ForEach-Object { Write-Host $_ }

# Check if valid UTF-8
$decoder = $utf8.GetDecoder()
$decoder.Fallback = [System.Text.DecoderReplacementFallback]::new('?')
$chars = New-Object char[] ($b.Length)
$charCount = $decoder.GetChars($b, 0, $b.Length, $chars, 0)
$decoded = New-Object string ($chars, 0, $charCount)
$replacements = ($decoded.ToCharArray() | Where-Object { $_ -eq '?' }).Count
Write-Host "`nReplacement chars in UTF-8 decode: $replacements"
