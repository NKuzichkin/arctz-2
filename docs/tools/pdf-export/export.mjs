// Экспорт markdown-файла в PDF (страницы формата A5) через системный Chrome.
// Вызывается из docs/export-user-guide-pdf.ps1, напрямую запускать вручную:
//   node export.mjs --input ../user-guide.md --output ../user-guide.pdf --chrome "C:\...\chrome.exe"
//
// Нумерация страниц идёт через footer-шаблон Puppeteer. Реальные номера страниц
// в оглавлении посчитать заранее (до печати) невозможно — Chrome/Puppeteer не
// поддерживает CSS target-counter(). Поэтому документ рендерится в PDF дважды:
// первый (черновой) проход нужен только чтобы узнать, на какую страницу попал
// каждый заголовок (по невидимым текстовым меткам PMARKERn, которые ищутся в
// уже готовом PDF через pdfjs-dist), второй проход — финальный PDF с
// подставленными номерами страниц в оглавлении.
import { readFile, writeFile, unlink } from "node:fs/promises";
import { dirname, resolve, extname } from "node:path";
import { pathToFileURL } from "node:url";
import { marked } from "marked";
import puppeteer from "puppeteer-core";
import { getDocument } from "pdfjs-dist/legacy/build/pdf.mjs";
import { PDFDocument } from "pdf-lib";

// Вёрстка под двустороннюю печать: поле подшивки 20 мм (слева на нечётных
// страницах, справа на чётных), поле со стороны обреза 8 мм.
// Chrome печатает все страницы с одинаковыми полями (опция margin в page.pdf),
// а @page :left / :right он игнорирует, поэтому печатаем с усреднённым боковым
// полем 14 мм — полоса набора получается ровно той же ширины, что и при
// зеркальных полях (на A5 это 148 - 20 - 8 = 120 мм), — и уже готовый PDF
// сдвигаем постранично на ±6 мм (см. applyMirroredMargins).
const BINDING_MARGIN_MM = 20;
const OUTER_MARGIN_MM = 8;
const SIDE_MARGIN_MM = (BINDING_MARGIN_MM + OUTER_MARGIN_MM) / 2;
const MIRROR_SHIFT_MM = (BINDING_MARGIN_MM - OUTER_MARGIN_MM) / 2;
const MM_TO_PT = 72 / 25.4;

const PDF_MARGIN = {
  top: "16mm",
  bottom: "18mm",
  left: `${SIDE_MARGIN_MM}mm`,
  right: `${SIDE_MARGIN_MM}mm`,
};

const FOOTER_TEMPLATE = `
<div style="width:100%; font-size:8pt; font-family:'Segoe UI',Arial,sans-serif; color:#666; text-align:center;">
  <span class="pageNumber"></span>
</div>`;
const HEADER_TEMPLATE = "<div></div>";

function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token.startsWith("--")) {
      args[token.slice(2)] = argv[i + 1];
      i += 1;
    }
  }
  return args;
}

// marked percent-кодирует кириллицу в href markdown-ссылок (в том числе в оглавлении
// и во внутренних перекрёстных ссылках вида "см. раздел 2"), а id заголовков (slugify)
// остаются обычным юникодом — без декодирования такие ссылки никуда не ведут.
function decodeInternalAnchors(html) {
  return html.replace(/href="#([^"]+)"/g, (match, hrefSlug) => `href="#${decodeURIComponent(hrefSlug)}"`);
}

// Тот же алгоритм слагификации заголовков, что используют GitHub/marked-gfm-heading-id
// (нужен, чтобы якоря вида "#7-переименование-сохранение-и-новая-программа" из оглавления
// исходного .md продолжали указывать на нужный заголовок в собранном HTML).
function slugify(headingText) {
  return headingText
    .toLowerCase()
    .replace(/<[^>]+>/g, "")
    .replace(/[.,()«»"':;!?]/g, "")
    .trim()
    .replace(/\s+/g, "-");
}

// Проставляет id каждому заголовку и вставляет перед его текстом невидимую
// (белый текст размером 1px, не влияет на вёрстку) метку PMARKERn — единственный
// надёжный способ понять постфактум, на какой физической странице PDF оказался
// заголовок, раз Chrome не поддерживает CSS target-counter().
function addHeadingIdsAndMarkers(html) {
  let counter = 0;
  const slugToMarker = new Map();
  const withIds = html.replace(/<h([1-6])>(.*?)<\/h\1>/gs, (match, level, inner) => {
    const plainText = inner.replace(/<[^>]+>/g, "");
    const slug = slugify(plainText);
    const marker = `PMARKER${counter}`;
    slugToMarker.set(slug, marker);
    counter += 1;
    return `<h${level} id="${slug}"><span class="pm">${marker}</span>${inner}</h${level}>`;
  });
  return { html: withIds, slugToMarker };
}

// Перестраивает список оглавления (после заголовка "Содержание") в вёрстку
// с точечной линией-заполнителем и номером страницы. Если pageNumbers не задан,
// колонка номера остаётся пустой, но резервирует то же место — это нужно, чтобы
// разбивка на страницы в черновом и финальном проходах совпадала.
function buildToc(html, pageNumbers) {
  const re = /<h2 id="содержание">([\s\S]*?)<\/h2>\s*<ol>([\s\S]*?)<\/ol>/;
  return html.replace(re, (match, headingInner, olInner) => {
    const items = [...olInner.matchAll(/<li>\s*<a href="#([^"]+)">([\s\S]*?)<\/a>\s*<\/li>/g)];
    const rows = items
      .map(([, rawHref, text]) => {
        // marked percent-кодирует кириллицу во href ссылок из markdown, а id заголовков
        // (см. slugify) остаются обычным юникодом — декодируем, чтобы ссылка реально
        // попадала на якорь заголовка и чтобы номер страницы находился по тому же ключу.
        const slug = decodeURIComponent(rawHref);
        const pageNum = pageNumbers ? pageNumbers.get(slug) ?? "" : "";
        return `<li class="toc-item"><a href="#${slug}">${text}</a><span class="toc-dots"></span><span class="toc-page">${pageNum}</span></li>`;
      })
      .join("\n");
    return `<h2 id="содержание">${headingInner}</h2>\n<ol class="toc">${rows}</ol>`;
  });
}

// marked оставляет относительные src как есть; страница грузится из временного HTML,
// поэтому переписываем их в абсолютные file:// пути относительно исходного .md.
function resolveImageSources(html, markdownDir) {
  return html.replace(/<img([^>]*?)src="([^"]+)"([^>]*)>/g, (match, before, src, after) => {
    if (/^([a-z]+:)?\/\//i.test(src) || src.startsWith("data:")) {
      return match;
    }
    const absolute = pathToFileURL(resolve(markdownDir, src)).href;
    return `<img${before}src="${absolute}"${after}>`;
  });
}

function wrapHtml(bodyHtml, title) {
  return `<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<title>${title}</title>
<style>
  body {
    font-family: "Segoe UI", Arial, sans-serif;
    font-size: 10.5pt;
    line-height: 1.45;
    color: #1a1a1a;
    margin: 0;
    /* Иконки-символы в тексте (▶ ⏸ ⏹ и т. п.) должны быть монохромными, как в
       приложении, а не превращаться Chrome в цветные эмодзи-глифы. */
    font-variant-emoji: text;
  }
  h1, h2, h3 { break-after: avoid; }
  h1 { font-size: 18pt; margin: 0 0 10pt; }
  /* Каждый раздел (## в markdown) начинается с новой страницы.
     Верхний отступ при этом лишний — он бы сдвинул заголовок вниз от поля. */
  h2 { font-size: 14pt; margin: 0 0 8pt; border-bottom: 1pt solid #ccc; padding-bottom: 3pt; break-before: page; }
  h3 { font-size: 12pt; margin: 14pt 0 6pt; }
  p, ul, ol, table, blockquote { margin: 0 0 8pt; }
  p, li { text-align: justify; }
  ul, ol { padding-left: 18pt; }
  a { color: #1a4fa0; text-decoration: none; }
  code {
    font-family: "Consolas", "Cascadia Mono", monospace;
    font-size: 9pt;
    background: #f0f0f0;
    padding: 1pt 3pt;
    border-radius: 2pt;
  }
  blockquote {
    margin-left: 0;
    padding: 4pt 10pt;
    border-left: 3pt solid #b8b8b8;
    color: #444;
    background: #fafafa;
  }
  hr { border: none; border-top: 1pt solid #ccc; margin: 14pt 0; }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 9.5pt;
    break-inside: avoid;
  }
  th, td {
    border: 1pt solid #ccc;
    padding: 4pt 6pt;
    text-align: left;
    vertical-align: top;
  }
  th { background: #f0f0f0; }
  img {
    display: block;
    max-width: 55%;
    height: auto;
    margin: 8pt auto;
    border: 1pt solid #ddd;
    break-inside: avoid;
  }
  /* Невидимая метка для определения номера страницы заголовка (см. addHeadingIdsAndMarkers) */
  .pm {
    font-size: 1px;
    line-height: 0;
    color: #fff;
    user-select: none;
  }
  ol.toc {
    list-style: decimal;
    padding-left: 18pt;
  }
  ol.toc li.toc-item {
    display: flex;
    align-items: baseline;
    margin: 0 0 5pt;
    break-inside: avoid;
  }
  ol.toc .toc-item a {
    white-space: nowrap;
  }
  ol.toc .toc-dots {
    flex: 1;
    margin: 0 4pt 2pt;
    border-bottom: 1pt dotted #999;
  }
  ol.toc .toc-page {
    min-width: 14pt;
    text-align: right;
    font-variant-numeric: tabular-nums;
  }
</style>
</head>
<body>
${bodyHtml}
</body>
</html>`;
}

// Ищет на каждой странице чернового PDF текстовые метки PMARKERn и возвращает
// Map<slug, номерСтраницы> (номер 1-based, совпадает с нумерацией в footer-шаблоне).
async function findMarkerPages(pdfPath, slugToMarker) {
  const data = new Uint8Array(await readFile(pdfPath));
  const pdfDocument = await getDocument({ data }).promise;
  const markerToSlug = new Map([...slugToMarker.entries()].map(([slug, marker]) => [marker, slug]));
  const pageNumbers = new Map();

  for (let pageIndex = 1; pageIndex <= pdfDocument.numPages; pageIndex += 1) {
    const page = await pdfDocument.getPage(pageIndex);
    const textContent = await page.getTextContent();
    const pageText = textContent.items.map((item) => item.str).join(" ");
    for (const [marker, slug] of markerToSlug) {
      if (!pageNumbers.has(slug) && pageText.includes(marker)) {
        pageNumbers.set(slug, pageIndex);
      }
    }
  }

  await pdfDocument.destroy();
  return pageNumbers;
}

// Превращает одинаковые боковые поля напечатанного PDF в зеркальные: содержимое
// страницы остаётся на месте, а сдвигается видимая область листа (MediaBox/CropBox).
// Сдвиг окна влево равнозначен сдвигу содержимого вправо, поэтому на нечётных
// страницах левое поле становится SIDE + SHIFT = 20 мм, правое SIDE - SHIFT = 8 мм,
// на чётных — наоборот. Колонтитул с номером страницы сдвигается вместе с текстом
// и остаётся по центру полосы набора.
async function applyMirroredMargins(pdfPath) {
  const pdfDocument = await PDFDocument.load(await readFile(pdfPath));
  const shiftPt = MIRROR_SHIFT_MM * MM_TO_PT;

  pdfDocument.getPages().forEach((page, pageIndex) => {
    const isOddPage = pageIndex % 2 === 0; // pageIndex 0 — первая, то есть нечётная страница
    const { x, y, width, height } = page.getMediaBox();
    const shiftedX = isOddPage ? x - shiftPt : x + shiftPt;
    page.setMediaBox(shiftedX, y, width, height);
    page.setCropBox(shiftedX, y, width, height);
  });

  await writeFile(pdfPath, await pdfDocument.save());
}

async function renderPdf(browser, htmlPath, outputPath) {
  const page = await browser.newPage();
  try {
    await page.goto(pathToFileURL(htmlPath).href, { waitUntil: "networkidle0" });
    await page.pdf({
      path: outputPath,
      format: "A5",
      printBackground: true,
      margin: PDF_MARGIN,
      displayHeaderFooter: true,
      headerTemplate: HEADER_TEMPLATE,
      footerTemplate: FOOTER_TEMPLATE,
    });
  } finally {
    await page.close();
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (!args.input || !args.output || !args.chrome) {
    console.error("Usage: node export.mjs --input <file.md> --output <file.pdf> --chrome <chrome.exe>");
    process.exitCode = 1;
    return;
  }

  const inputPath = resolve(args.input);
  const outputPath = resolve(args.output);
  const markdownDir = dirname(inputPath);
  const title = args.title ?? "Документ";

  const markdownSource = await readFile(inputPath, "utf8");
  const bodyHtml = decodeInternalAnchors(await marked.parse(markdownSource, { gfm: true }));
  const { html: htmlWithIds, slugToMarker } = addHeadingIdsAndMarkers(bodyHtml);

  const draftHtml = wrapHtml(resolveImageSources(buildToc(htmlWithIds, null), markdownDir), title);
  const draftHtmlPath = resolve(markdownDir, `.${Date.now()}-pdf-draft.html`);
  const draftPdfPath = resolve(markdownDir, `.${Date.now()}-pdf-draft.pdf`);
  const finalHtmlPath = resolve(markdownDir, `.${Date.now()}-pdf-export.html`);

  const browser = await puppeteer.launch({
    executablePath: args.chrome,
    headless: true,
  });

  try {
    await writeFile(draftHtmlPath, draftHtml, "utf8");
    await renderPdf(browser, draftHtmlPath, draftPdfPath);

    const pageNumbers = await findMarkerPages(draftPdfPath, slugToMarker);

    const finalHtml = wrapHtml(resolveImageSources(buildToc(htmlWithIds, pageNumbers), markdownDir), title);
    await writeFile(finalHtmlPath, finalHtml, "utf8");
    await renderPdf(browser, finalHtmlPath, outputPath);
  } finally {
    await browser.close();
    await unlink(draftHtmlPath).catch(() => {});
    await unlink(draftPdfPath).catch(() => {});
    await unlink(finalHtmlPath).catch(() => {});
  }

  await applyMirroredMargins(outputPath);

  console.log(`PDF сохранён: ${outputPath}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
