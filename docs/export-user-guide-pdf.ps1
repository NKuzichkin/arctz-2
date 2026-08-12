<#
.SYNOPSIS
    Экспортирует docs/user-guide.md в PDF со страницами формата A5.
.DESCRIPTION
    Конвертирует markdown в HTML (пакет marked) и печатает его в PDF через
    системный Google Chrome в headless-режиме (puppeteer-core, страница
    жёстко задана как A5 — это надёжнее, чем полагаться на @page CSS,
    который простой `chrome --print-to-pdf` игнорирует).
    Вёрстка рассчитана на двустороннюю печать: поле подшивки 20 мм (слева на
    нечётных страницах, справа на чётных) и 8 мм со стороны обреза.
    Зависимости (marked, puppeteer-core, pdfjs-dist, pdf-lib) ставятся в
    docs/tools/pdf-export/node_modules при первом запуске.
.PARAMETER OutputPath
    Путь к результирующему PDF. По умолчанию docs/user-guide.pdf.
.PARAMETER ChromePath
    Путь к chrome.exe. По умолчанию ищется в стандартных папках установки
    Google Chrome, затем Microsoft Edge как запасной вариант.
.EXAMPLE
    ./docs/export-user-guide-pdf.ps1
.EXAMPLE
    ./docs/export-user-guide-pdf.ps1 -OutputPath C:\Temp\guide.pdf
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$ChromePath
)

$ErrorActionPreference = "Stop"

$docsDir = $PSScriptRoot
$inputPath = Join-Path $docsDir "user-guide.md"
if (-not (Test-Path $inputPath)) {
    throw "Не найден $inputPath"
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $docsDir "user-guide.pdf"
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js не найден в PATH. Установите Node.js (https://nodejs.org/) и повторите."
}

if (-not $ChromePath) {
    $candidates = @(
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    )
    $ChromePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $ChromePath) {
        throw "Не найден Chrome/Edge ни в одной из стандартных папок. Укажите путь явно: -ChromePath 'C:\...\chrome.exe'"
    }
}

$toolDir = Join-Path $docsDir "tools\pdf-export"
$nodeModules = Join-Path $toolDir "node_modules"
# Проверяем не только наличие node_modules, но и каждую зависимость из package.json:
# иначе пакет, добавленный в package.json позже первого запуска, не доустановится.
$dependencies = (Get-Content (Join-Path $toolDir "package.json") -Raw | ConvertFrom-Json).dependencies
$missingPackages = @($dependencies.PSObject.Properties.Name | Where-Object { -not (Test-Path (Join-Path $nodeModules $_)) })
if ($missingPackages.Count -gt 0) {
    Write-Host "Устанавливаю зависимости ($($missingPackages -join ', ')) в $toolDir..."
    Push-Location $toolDir
    try {
        npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) {
            throw "npm install завершился с ошибкой"
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Экспортирую $inputPath -> $OutputPath (A5, зеркальные поля 20/8 мм, Chrome: $ChromePath)"
node (Join-Path $toolDir "export.mjs") --input $inputPath --output $OutputPath --chrome $ChromePath --title "ArctZ — руководство пользователя"
if ($LASTEXITCODE -ne 0) {
    throw "Экспорт в PDF завершился с ошибкой"
}

Write-Host "Готово: $OutputPath"
