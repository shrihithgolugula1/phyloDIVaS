import React from 'react';

// Render report Markdown as React text/elements. Raw HTML is never executed.
const inline=text=>String(text).split(/(\*\*[^*]+\*\*|`[^`]+`)/g).map((part,i)=>part.startsWith('**')?<strong key={i}>{part.slice(2,-2)}</strong>:part.startsWith('`')?<code key={i}>{part.slice(1,-1)}</code>:part);
export default function MarkdownReport({text}) {
 const lines=String(text||'').split(/\r?\n/),blocks=[];let i=0;
 while(i<lines.length){
  const line=lines[i];if(!line.trim()){i++;continue;}
  if(/^```/.test(line)){const content=[];i++;while(i<lines.length&&!/^```/.test(lines[i]))content.push(lines[i++]);i++;blocks.push(<pre key={blocks.length}>{content.join('\n')}</pre>);continue;}
  if(/^#{1,6}\s/.test(line)||i+1<lines.length&&/^[-=]{3,}\s*$/.test(lines[i+1])){blocks.push(<h6 key={blocks.length}>{inline(line.replace(/^#{1,6}\s*/,''))}</h6>);i+=/^#{1,6}\s/.test(line)?1:2;continue;}
  if(line.includes('|')&&i+1<lines.length&&/^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)+\|?\s*$/.test(lines[i+1])){
   const cells=value=>value.trim().replace(/^\||\|$/g,'').split('|').map(s=>s.trim()),headers=cells(line),rows=[];i+=2;
   while(i<lines.length&&lines[i].includes('|')&&lines[i].trim())rows.push(cells(lines[i++]));
   blocks.push(<div key={blocks.length} style={{overflowX:'auto'}}><table className="table table-sm"><thead><tr>{headers.map((cell,k)=><th key={k} scope="col">{inline(cell)}</th>)}</tr></thead><tbody>{rows.map((row,k)=><tr key={k}>{row.map((cell,j)=><td key={j}>{inline(cell)}</td>)}</tr>)}</tbody></table></div>);continue;
  }
  if(/^\s*[-*+]\s/.test(line)){const items=[];while(i<lines.length&&/^\s*[-*+]\s/.test(lines[i]))items.push(lines[i++].replace(/^\s*[-*+]\s/,''));blocks.push(<ul key={blocks.length}>{items.map((item,k)=><li key={k}>{inline(item)}</li>)}</ul>);continue;}
  const paragraph=[line];i++;while(i<lines.length&&lines[i].trim()&&!/^(#|```|[-*+]\s)/.test(lines[i])&&!(i+1<lines.length&&/^[-=]{3,}\s*$/.test(lines[i+1])))paragraph.push(lines[i++]);blocks.push(<p key={blocks.length}>{inline(paragraph.join(' '))}</p>);
 }
 return <div style={{maxHeight:'30rem',overflow:'auto',padding:12}} tabIndex="0" aria-label="Scrollable tool Markdown report">{blocks}</div>;
}
