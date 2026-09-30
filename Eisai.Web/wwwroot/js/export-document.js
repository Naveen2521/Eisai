(() => {
  const encoder = new TextEncoder();

  function xmlEscape(value) {
    return String(value ?? "")
      .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function excelCommandText(value) {
    return String(value ?? "").replace(/&/g, "&&").replace(/</g, "").replace(/>/g, "");
  }

  function pdfText(value) {
    return String(value ?? "")
      .replace(/[^\x20-\x7E]/g, " ")
      .replace(/\\/g, "\\\\")
      .replace(/\(/g, "\\(")
      .replace(/\)/g, "\\)");
  }

  function fitPdfText(value, width, fontSize) {
    const text = String(value ?? "").replace(/[^\x20-\x7E]/g, " ");
    const max = Math.max(1, Math.floor(width / (fontSize * 0.52)));
    if (text.length <= max) {
      return text;
    }
    return `${text.slice(0, Math.max(1, max - 3))}...`;
  }

  function generatedLabel() {
    const formatted = new Date().toLocaleString("en-GB", {
      day: "2-digit",
      month: "short",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit"
    });
    return `Generated on ${formatted}`;
  }

  function logoElement() {
    return document.querySelector(".brand-logo-collapsed") || document.querySelector(".brand-logo-expanded");
  }

  function saveBlob(blob, name) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
  }

  function columnLetter(index) {
    let n = index + 1;
    let name = "";
    while (n > 0) {
      const rem = (n - 1) % 26;
      name = String.fromCharCode(65 + rem) + name;
      n = Math.floor((n - 1) / 26);
    }
    return name;
  }

  function excelColumnPixels(width) {
    return Math.floor(((256 * width + Math.floor(128 / 7)) / 256) * 7);
  }

  function pngSize(bytes) {
    if (!bytes || bytes.length < 24 || bytes[0] !== 0x89) {
      return { width: 48, height: 48 };
    }
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    return { width: view.getUint32(16), height: view.getUint32(20) };
  }

  function crc32(bytes) {
    let crc = 0xffffffff;
    for (let i = 0; i < bytes.length; i += 1) {
      crc ^= bytes[i];
      for (let bit = 0; bit < 8; bit += 1) {
        crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
      }
    }
    return (crc ^ 0xffffffff) >>> 0;
  }

  function concatBytes(parts) {
    const total = parts.reduce((sum, part) => sum + part.length, 0);
    const out = new Uint8Array(total);
    let offset = 0;
    parts.forEach(part => {
      out.set(part, offset);
      offset += part.length;
    });
    return out;
  }

  function zipStore(files) {
    const locals = [];
    const central = [];
    let offset = 0;
    files.forEach(file => {
      const name = encoder.encode(file.name);
      const crc = crc32(file.data);
      const local = new Uint8Array(30 + name.length);
      const view = new DataView(local.buffer);
      view.setUint32(0, 0x04034b50, true);
      view.setUint16(4, 20, true);
      view.setUint16(8, 0, true);
      view.setUint32(14, crc, true);
      view.setUint32(18, file.data.length, true);
      view.setUint32(22, file.data.length, true);
      view.setUint16(26, name.length, true);
      local.set(name, 30);
      locals.push(local, file.data);

      const header = new Uint8Array(46 + name.length);
      const centralView = new DataView(header.buffer);
      centralView.setUint32(0, 0x02014b50, true);
      centralView.setUint16(4, 20, true);
      centralView.setUint16(6, 20, true);
      centralView.setUint32(16, crc, true);
      centralView.setUint32(20, file.data.length, true);
      centralView.setUint32(24, file.data.length, true);
      centralView.setUint16(28, name.length, true);
      centralView.setUint32(42, offset, true);
      header.set(name, 46);
      central.push(header);
      offset += local.length + file.data.length;
    });

    const centralBytes = concatBytes(central);
    const end = new Uint8Array(22);
    const endView = new DataView(end.buffer);
    endView.setUint32(0, 0x06054b50, true);
    endView.setUint16(8, files.length, true);
    endView.setUint16(10, files.length, true);
    endView.setUint32(12, centralBytes.length, true);
    endView.setUint32(16, offset, true);
    return concatBytes([...locals, centralBytes, end]);
  }

  function xml(text) {
    return encoder.encode(text);
  }

  function inlineCell(ref, value, style) {
    const styleAttr = style ? ` s="${style}"` : "";
    return `<c r="${ref}" t="inlineStr"${styleAttr}><is><t xml:space="preserve">${xmlEscape(value)}</t></is></c>`;
  }

  async function loadLogo() {
    const image = logoElement();
    const url = image?.currentSrc || image?.src || "";
    if (!url) {
      return null;
    }
    try {
      const response = await fetch(url);
      if (!response.ok) {
        return null;
      }
      const bytes = new Uint8Array(await response.arrayBuffer());
      const raster = await rasterizeLogo(url);
      return raster ? { bytes, raster } : null;
    } catch {
      return null;
    }
  }

  function rasterizeLogo(url) {
    return new Promise(resolve => {
      const image = new Image();
      image.onload = () => {
        const maxEdge = 128;
        const scale = Math.min(maxEdge / image.width, maxEdge / image.height, 1);
        const width = Math.max(1, Math.round(image.width * scale));
        const height = Math.max(1, Math.round(image.height * scale));
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d");
        context.fillStyle = "#ffffff";
        context.fillRect(0, 0, width, height);
        context.drawImage(image, 0, 0, width, height);
        const pixels = context.getImageData(0, 0, width, height).data;
        const rgb = new Uint8Array(width * height * 3);
        for (let i = 0, j = 0; i < pixels.length; i += 4, j += 3) {
          const alpha = pixels[i + 3] / 255;
          rgb[j] = Math.round(pixels[i] * alpha + 255 * (1 - alpha));
          rgb[j + 1] = Math.round(pixels[i + 1] * alpha + 255 * (1 - alpha));
          rgb[j + 2] = Math.round(pixels[i + 2] * alpha + 255 * (1 - alpha));
        }
        resolve({ width, height, rgb });
      };
      image.onerror = () => resolve(null);
      image.src = url;
    });
  }

  async function deflateBytes(bytes) {
    const stream = new Blob([bytes]).stream().pipeThrough(new CompressionStream("deflate"));
    return new Uint8Array(await new Response(stream).arrayBuffer());
  }

  function pdfStream(dictionary, bytes) {
    const head = encoder.encode(`<< ${dictionary} /Length ${bytes.length} >>\nstream\n`);
    const tail = encoder.encode("\nendstream");
    return concatBytes([head, bytes, tail]);
  }

  function buildPdf(objects, catalogId) {
    const header = encoder.encode("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
    const parts = [header];
    const offsets = [0];
    let cursor = header.length;
    objects.forEach((content, index) => {
      offsets.push(cursor);
      const body = typeof content === "string" ? encoder.encode(content) : content;
      const chunk = concatBytes([
        encoder.encode(`${index + 1} 0 obj\n`),
        body,
        encoder.encode("\nendobj\n")
      ]);
      parts.push(chunk);
      cursor += chunk.length;
    });
    let xref = `xref\n0 ${objects.length + 1}\n`;
    xref += "0000000000 65535 f \r\n";
    offsets.slice(1).forEach(offset => {
      xref += `${String(offset).padStart(10, "0")} 00000 n \r\n`;
    });
    xref += `trailer\n<< /Size ${objects.length + 1} /Root ${catalogId} 0 R >>\nstartxref\n${cursor}\n%%EOF`;
    parts.push(encoder.encode(xref));
    return concatBytes(parts);
  }

  function reportRows(headers, rows) {
    const safeHeaders = headers.length ? headers : ["Details"];
    const safeRows = rows.length ? rows : [["No records"]];
    return { headers: safeHeaders, rows: safeRows.map(row => safeHeaders.map((_, index) => row[index] ?? "")) };
  }

  async function downloadExcel({ title, headers, rows, fileName }) {
    const report = reportRows(headers, rows);
    const stamp = generatedLabel();
    const logo = await loadLogo();
    const columnCount = report.headers.length;
    const widths = report.headers.map((header, index) => {
      const longest = Math.max(
        String(header).length,
        ...report.rows.slice(0, 80).map(row => String(row[index] ?? "").length)
      );
      return Math.min(40, Math.max(14, longest + 2));
    });
    if (logo) {
      const size = pngSize(logo.bytes);
      const imagePixels = Math.min(120, Math.max(36, Math.round(36 * (size.width / Math.max(size.height, 1)))));
      widths[widths.length - 1] = Math.max(widths[widths.length - 1], Math.ceil(imagePixels / 7) + 2);
    }

    const lastColumn = columnLetter(columnCount - 1);
    const lastDataRow = 2 + report.rows.length;
    const footerRow = lastDataRow + 2;
    const sheetRows = [
      `<row r="1" ht="32" customHeight="1"><c r="A1" t="inlineStr" s="1"><is><t xml:space="preserve">${xmlEscape(title || "Report")}</t></is></c></row>`,
      `<row r="2" ht="20" customHeight="1">${report.headers.map((header, index) => inlineCell(`${columnLetter(index)}2`, header, 2)).join("")}</row>`
    ];
    report.rows.forEach((row, rowIndex) => {
      const excelRow = rowIndex + 3;
      sheetRows.push(`<row r="${excelRow}">${row.map((value, index) => inlineCell(`${columnLetter(index)}${excelRow}`, value)).join("")}</row>`);
    });
    sheetRows.push(`<row r="${footerRow}" ht="20" customHeight="1">${inlineCell(`A${footerRow}`, stamp, 3)}</row>`);

    const merges = columnCount > 1 ? `<mergeCells count="1"><mergeCell ref="A1:${lastColumn}1"/></mergeCells>` : "";
    const drawing = logo ? `<drawing r:id="rId1"/>` : "";
    const sheet = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>
  <dimension ref="A1:${lastColumn}${footerRow}"/>
  <sheetViews><sheetView workbookViewId="0"><pane ySplit="2" topLeftCell="A3" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>
  <sheetFormatPr defaultRowHeight="18"/>
  <cols>${widths.map((width, index) => `<col min="${index + 1}" max="${index + 1}" width="${width}" customWidth="1"/>`).join("")}</cols>
  <sheetData>${sheetRows.join("")}</sheetData>
  ${merges}
  <pageMargins left="0.5" right="0.5" top="0.55" bottom="0.55" header="0.25" footer="0.3"/>
  <pageSetup orientation="landscape" paperSize="9" fitToWidth="1" fitToHeight="0"/>
  <headerFooter>
    <oddFooter>&amp;L${xmlEscape(excelCommandText(stamp))}&amp;RPage &amp;P of &amp;N</oddFooter>
  </headerFooter>
  ${drawing}
</worksheet>`;

    const styles = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="4">
    <font><sz val="11"/><name val="Calibri"/></font>
    <font><b/><sz val="16"/><color rgb="FF101828"/><name val="Calibri"/></font>
    <font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
    <font><i/><sz val="10"/><color rgb="FF667085"/><name val="Calibri"/></font>
  </fonts>
  <fills count="3">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF0E6B64"/><bgColor indexed="64"/></patternFill></fill>
  </fills>
  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="4">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="center"/></xf>
    <xf numFmtId="0" fontId="2" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
    <xf numFmtId="0" fontId="3" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="center"/></xf>
  </cellXfs>
  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>`;

    const files = [
      { name: "[Content_Types].xml", data: xml(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  ${logo ? `<Default Extension="png" ContentType="image/png"/>` : ""}
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
  ${logo ? `<Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>` : ""}
</Types>`) },
      { name: "_rels/.rels", data: xml(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>`) },
      { name: "xl/workbook.xml", data: xml(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets><sheet name="Report" sheetId="1" r:id="rId1"/></sheets>
  <definedNames>
    <definedName name="_xlnm.Print_Titles" localSheetId="0">Report!$1:$2</definedName>
    <definedName name="_xlnm.Print_Area" localSheetId="0">Report!$A$1:$${lastColumn}$${lastDataRow}</definedName>
  </definedNames>
</workbook>`) },
      { name: "xl/_rels/workbook.xml.rels", data: xml(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>`) },
      { name: "xl/styles.xml", data: xml(styles) },
      { name: "xl/worksheets/sheet1.xml", data: xml(sheet) }
    ];

    if (logo) {
      const size = pngSize(logo.bytes);
      const targetHeight = 28 * 9525;
      let cx = Math.round(size.width * (targetHeight / Math.max(size.height, 1)));
      let cy = targetHeight;
      const maxWidth = 120 * 9525;
      if (cx > maxWidth) {
        cy = Math.round(cy * (maxWidth / cx));
        cx = maxWidth;
      }
      const pixels = widths.map(excelColumnPixels);
      const totalPixels = pixels.reduce((sum, value) => sum + value, 0);
      const leftPixels = Math.max(8, totalPixels - Math.round(cx / 9525) - 10);
      let used = 0;
      let anchorColumn = 0;
      for (let index = 0; index < pixels.length; index += 1) {
        if (used + pixels[index] > leftPixels) {
          anchorColumn = index;
          break;
        }
        used += pixels[index];
        anchorColumn = index;
      }
      const columnOffset = Math.max(0, Math.round((leftPixels - used) * 9525));
      files.push(
        { name: "xl/worksheets/_rels/sheet1.xml.rels", data: xml(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
</Relationships>`) },
        { name: "xl/drawings/drawing1.xml", data: xml(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <xdr:oneCellAnchor>
    <xdr:from><xdr:col>${anchorColumn}</xdr:col><xdr:colOff>${columnOffset}</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>60000</xdr:rowOff></xdr:from>
    <xdr:ext cx="${cx}" cy="${cy}"/>
    <xdr:pic>
      <xdr:nvPicPr><xdr:cNvPr id="2" name="Logo"/><xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr></xdr:nvPicPr>
      <xdr:blipFill><a:blip r:embed="rId1"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>
      <xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="${cx}" cy="${cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr>
    </xdr:pic>
    <xdr:clientData/>
  </xdr:oneCellAnchor>
</xdr:wsDr>`) },
        { name: "xl/drawings/_rels/drawing1.xml.rels", data: xml(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/logo.png"/>
</Relationships>`) },
        { name: "xl/media/logo.png", data: logo.bytes }
      );
    }

    const workbook = zipStore(files);
    saveBlob(new Blob([workbook], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }), fileName);
  }

  function columnWidths(headers, rows, usable) {
    const weights = headers.map((header, index) => {
      const sample = Math.max(
        String(header).length,
        ...rows.slice(0, 40).map(row => String(row[index] ?? "").length)
      );
      return Math.min(32, Math.max(6, sample));
    });
    const total = weights.reduce((sum, weight) => sum + weight, 0);
    return weights.map(weight => (weight / total) * usable);
  }

  async function downloadPdf({ title, headers, rows, fileName }) {
    const report = reportRows(headers, rows);
    const stamp = generatedLabel();
    const logo = await loadLogo();
    const pageWidth = 842;
    const pageHeight = 595;
    const margin = 32;
    const headerLineY = pageHeight - 54;
    const footerLineY = 34;
    const rowHeight = 16;
    const columnGap = 8;
    const usable = pageWidth - margin * 2;
    const widths = columnWidths(report.headers, report.rows, usable);
    const dataTop = headerLineY - 40;
    const dataBottom = footerLineY + 12;
    const perPage = Math.max(1, Math.floor((dataTop - dataBottom) / rowHeight));
    const pages = [];
    for (let index = 0; index < report.rows.length; index += perPage) {
      pages.push(report.rows.slice(index, index + perPage));
    }

    let icon = null;
    if (logo?.raster) {
      const imageBytes = await deflateBytes(logo.raster.rgb);
      icon = { ...logo.raster, bytes: imageBytes };
    }

    const objects = [];
    const add = content => {
      objects.push(content);
      return objects.length;
    };
    const fontId = add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
    const boldId = add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
    const imageId = icon
      ? add(pdfStream(` /Type /XObject /Subtype /Image /Width ${icon.width} /Height ${icon.height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode`, icon.bytes))
      : 0;

    let iconHeight = 28;
    let iconWidth = icon ? iconHeight * (icon.width / icon.height) : 28;
    if (icon && iconWidth > 110) {
      iconWidth = 110;
      iconHeight = iconWidth * (icon.height / icon.width);
    }
    const iconX = pageWidth - margin - iconWidth;
    const iconY = headerLineY + 10;

    const pageIds = pages.map((pageRows, pageIndex) => {
      const commands = [
        "0.075 0.769 0.690 RG",
        "1.2 w",
        `${margin} ${headerLineY} m ${pageWidth - margin} ${headerLineY} l S`,
        "0.063 0.094 0.157 rg",
        "BT",
        "/F2 16 Tf",
        `1 0 0 1 ${margin} ${headerLineY + 16} Tm (${pdfText(fitPdfText(title || "Report", iconX - margin - 16, 16))}) Tj`,
        "ET"
      ];
      if (icon) {
        commands.push(
          "q",
          `${iconWidth.toFixed(2)} 0 0 ${iconHeight.toFixed(2)} ${iconX.toFixed(2)} ${iconY.toFixed(2)} cm`,
          "/Im1 Do",
          "Q"
        );
      } else {
        const mark = pdfText((title || "D").trim().charAt(0).toUpperCase() || "D");
        commands.push(
          "0.075 0.769 0.690 rg",
          `${iconX} ${iconY} ${iconWidth} ${iconHeight} re f`,
          "1 1 1 rg",
          "BT",
          "/F2 12 Tf",
          `1 0 0 1 ${iconX + 9} ${iconY + 9} Tm (${mark}) Tj`,
          "ET"
        );
      }

      const headerY = headerLineY - 22;
      commands.push(
        "0.055 0.420 0.392 rg",
        `${margin} ${headerY - 5} ${usable} 18 re f`,
        "1 1 1 rg",
        "BT",
        "/F2 9 Tf"
      );
      let cursor = margin;
      report.headers.forEach((header, index) => {
        const text = pdfText(fitPdfText(header, widths[index] - columnGap, 9));
        commands.push(`1 0 0 1 ${cursor + 4} ${headerY} Tm (${text}) Tj`);
        cursor += widths[index];
      });
      commands.push("ET", "0.063 0.094 0.157 rg", "BT", "/F1 9 Tf");
      pageRows.forEach((row, rowIndex) => {
        const y = dataTop - rowIndex * rowHeight;
        if (rowIndex % 2 === 1) {
          commands.push("ET", "0.957 0.969 0.980 rg", `${margin} ${y - 4} ${usable} ${rowHeight} re f`, "0.063 0.094 0.157 rg", "BT", "/F1 9 Tf");
        }
        cursor = margin;
        row.forEach((value, index) => {
          const text = pdfText(fitPdfText(value, widths[index] - columnGap, 9));
          commands.push(`1 0 0 1 ${cursor + 4} ${y} Tm (${text}) Tj`);
          cursor += widths[index];
        });
      });
      const pageLabel = `Page ${pageIndex + 1} of ${pages.length}`;
      const pageLabelX = pageWidth - margin - pageLabel.length * 4.6;
      commands.push(
        "ET",
        "0.75 0.78 0.82 RG",
        "0.8 w",
        `${margin} ${footerLineY} m ${pageWidth - margin} ${footerLineY} l S`,
        "0.400 0.439 0.522 rg",
        "BT",
        "/F1 9 Tf",
        `1 0 0 1 ${margin} 18 Tm (${pdfText(stamp)}) Tj`,
        `1 0 0 1 ${pageLabelX.toFixed(2)} 18 Tm (${pdfText(pageLabel)}) Tj`,
        "ET"
      );

      const content = `${commands.join("\n")}\n`;
      const contentId = add(pdfStream("", encoder.encode(content)));
      const resources = imageId
        ? `/Resources << /Font << /F1 ${fontId} 0 R /F2 ${boldId} 0 R >> /XObject << /Im1 ${imageId} 0 R >> >>`
        : `/Resources << /Font << /F1 ${fontId} 0 R /F2 ${boldId} 0 R >> >>`;
      return add(`<< /Type /Page /Parent PAGES 0 R /MediaBox [0 0 ${pageWidth} ${pageHeight}] /Contents ${contentId} 0 R ${resources} >>`);
    });

    const pagesId = add(`<< /Type /Pages /Count ${pageIds.length} /Kids [${pageIds.map(id => `${id} 0 R`).join(" ")}] >>`);
    const catalogId = add(`<< /Type /Catalog /Pages ${pagesId} 0 R >>`);
    pageIds.forEach(id => {
      objects[id - 1] = objects[id - 1].replace("Parent PAGES 0 R", `Parent ${pagesId} 0 R`);
    });
    const bytes = buildPdf(objects, catalogId);
    saveBlob(new Blob([bytes], { type: "application/pdf" }), fileName);
  }

  window.darExport = { downloadExcel, downloadPdf };
})();
