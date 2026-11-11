import PropTypes from "prop-types";
import React, { useMemo, useRef, useState } from "react";
import Button from "react-bootstrap/Button";
import { downloadSvg, downloadSvgPng, downloadText } from "../../../utils/downloads";
import {
    evaluateGeneEvidence,
    overlapCounts,
    rowsToDelimited
} from "../../../utils/hyphyDashboard";
import styles from "./AnalysisResults.module.css";

const formatNumber = (value, digits = 3) => {
    if (value === null || value === undefined || !Number.isFinite(Number(value))) return "N/A";
    const number = Number(value);
    if (number === 0) return "0";
    return Math.abs(number) < 0.001 ? number.toExponential(2) : number.toPrecision(digits);
};

const safeName = (value) => String(value || "figure").replace(/[^a-z0-9._-]+/gi, "_");

function FigureShell({ title, description, count, filename, svgRef, csv, children, note }) {
    return (
        <section className={styles.dashboardFigure} aria-labelledby={`${filename}-title`}>
            <div className={styles.figureHeader}>
                <div>
                    <h5 id={`${filename}-title`}>{title}</h5>
                    <p>{description}</p>
                </div>
                <span className={styles.figureDenominator}>{count.toLocaleString()} genes</span>
            </div>
            {children}
            {note && <p className={styles.figureNote}>{note}</p>}
            <div className={styles.figureActions} aria-label={`Download ${title}`}>
                <Button size="sm" variant="outline-secondary" onClick={() => downloadSvg(svgRef.current, `${safeName(filename)}.svg`)}>SVG</Button>
                <Button size="sm" variant="outline-secondary" onClick={() => downloadSvgPng(svgRef.current, `${safeName(filename)}.png`)}>PNG</Button>
                <Button size="sm" variant="outline-secondary" onClick={() => downloadText(csv, `${safeName(filename)}.csv`, "text/csv;charset=utf-8")}>CSV</Button>
            </div>
        </section>
    );
}

FigureShell.propTypes = {
    title: PropTypes.string.isRequired,
    description: PropTypes.string.isRequired,
    count: PropTypes.number.isRequired,
    filename: PropTypes.string.isRequired,
    svgRef: PropTypes.shape({ current: PropTypes.any }).isRequired,
    csv: PropTypes.string.isRequired,
    children: PropTypes.node.isRequired,
    note: PropTypes.string
};

FigureShell.defaultProps = { note: null };

export function BustedHistogram({ genes, thresholds, onOpenGene }) {
    const svgRef = useRef(null);
    const [countScale,setCountScale]=useState("log");
    const values = useMemo(() => genes.flatMap((gene) => {
        if (gene.bustedPValue == null || !Number.isFinite(Number(gene.bustedPValue)) || gene.bustedPValue < 0 || gene.bustedPValue > 1) return [];
        return [{
            gene,
            pValue: Number(gene.bustedPValue),
            qValue: gene.bustedQValue,
            score: Math.min(12, -Math.log10(Math.max(Number(gene.bustedPValue), 1e-12)))
        }];
    }), [genes]);
    const bins = useMemo(() => {
        const result = Array.from({ length: 12 }, (_, index) => ({
            start: index,
            end: index + 1,
            count: 0,
            fdrCount: 0,
            genes: []
        }));
        values.forEach((value) => {
            const bin = result[Math.min(11, Math.floor(value.score))];
            bin.count += 1;
            bin.genes.push(value.gene);
            if (value.qValue != null && Number(value.qValue) <= thresholds.bustedQ) bin.fdrCount += 1;
        });
        return result;
    }, [values, thresholds.bustedQ]);
    const maximum = Math.max(1, ...bins.map((bin) => bin.count));
    const width = 720;
    const height = 310;
    const plot = { left: 56, top: 22, right: 18, bottom: 52 };
    const chartWidth = width - plot.left - plot.right;
    const chartHeight = height - plot.top - plot.bottom;
    const barWidth = chartWidth / bins.length;
    const scaleCount=n=>(countScale==="log"?Math.log1p(n)/Math.log1p(maximum):n/maximum)*chartHeight;
    const countTicks=countScale==="log"?[0,1,10,100,1000,10000].filter(n=>n<=maximum):[0,.25,.5,.75,1].map(f=>Math.round(f*maximum));
    const thresholdX = plot.left + Math.min(12, -Math.log10(thresholds.bustedP)) / 12 * chartWidth;
    const csv = rowsToDelimited(values.map((value) => ({
        gene: value.gene.displayName,
        orthogroup: value.gene.orthogroupId,
        pValue: value.pValue,
        qValue: value.qValue,
        fdrSignificant: value.qValue != null && Number(value.qValue) <= thresholds.bustedQ
    })), [
        { label: "Gene", value: "gene" },
        { label: "Orthogroup", value: "orthogroup" },
        { label: "BUSTED p-value", value: "pValue" },
        { label: "BH q-value", value: "qValue" },
        { label: "FDR significant", value: "fdrSignificant" }
    ]);

    return (
        <FigureShell
            title="BUSTED p-value distribution"
            description="All readable BUSTED tests, grouped by evidence strength. Paired bars compare all genes (gray) with genes passing BH FDR (green)."
            count={values.length}
            filename="busted_p_value_distribution"
            svgRef={svgRef}
            csv={csv}
            note={`The vertical line marks raw p = ${thresholds.bustedP}. BH uses the full valid result family shown above, not this display subset. Genuine zero p-values are displayed at 12 using a 1e-12 plotting floor; exported p-values remain unchanged. Missing p-values are excluded.`}
        >
            <label>Gene-count scale <select value={countScale} onChange={e=>setCountScale(e.target.value)}><option value="log">Logarithmic (reveals smaller counts)</option><option value="linear">Linear</option></select></label>
            <svg ref={svgRef} className={styles.figureSvg} viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby="busted-hist-title busted-hist-desc">
                <title id="busted-hist-title">BUSTED p-value histogram</title>
                <desc id="busted-hist-desc">Histogram of minus log ten BUSTED p-values for {values.length} genes.</desc>
                <line x1={plot.left} y1={plot.top + chartHeight} x2={width - plot.right} y2={plot.top + chartHeight} className={styles.axisLine} />
                <line x1={plot.left} y1={plot.top} x2={plot.left} y2={plot.top + chartHeight} className={styles.axisLine} />
                {countTicks.map(n=><g key={n}><line x1={plot.left} x2={width-plot.right} y1={plot.top+chartHeight-scaleCount(n)} y2={plot.top+chartHeight-scaleCount(n)} className={styles.gridLine}/><text x={plot.left-6} y={plot.top+chartHeight-scaleCount(n)+4} textAnchor="end" className={styles.axisText}>{n.toLocaleString()}</text></g>)}
                {bins.map((bin, index) => {
                    const totalHeight = scaleCount(bin.count);
                    const fdrHeight = scaleCount(bin.fdrCount);
                    const x = plot.left + index * barWidth + 2;
                    const y = plot.top + chartHeight - totalHeight;
                    return (
                        <g key={bin.start} role={bin.genes.length === 1 ? "button" : undefined} tabIndex={bin.genes.length === 1 ? 0 : undefined}
                            onClick={() => bin.genes.length === 1 && onOpenGene(bin.genes[0])}
                            onKeyDown={(event) => {
                                if (bin.genes.length === 1 && (event.key === "Enter" || event.key === " ")) onOpenGene(bin.genes[0]);
                            }}>
                            <title>{`${bin.count} genes from ${bin.start} to ${bin.end} -log10(p); ${bin.fdrCount} pass FDR`}</title>
                            <rect x={x} y={y} width={Math.max(1, (barWidth - 6)/2)} height={totalHeight} className={styles.histogramBar} />
                            <rect x={x+(barWidth-6)/2+2} y={plot.top + chartHeight - fdrHeight} width={Math.max(1, (barWidth - 6)/2)} height={fdrHeight} className={styles.histogramSignificant} />
                            {index % 2 === 0 && <text x={x + barWidth / 2} y={height - 28} textAnchor="middle" className={styles.axisText}>{bin.start}</text>}
                        </g>
                    );
                })}
                <line x1={thresholdX} y1={plot.top} x2={thresholdX} y2={plot.top + chartHeight} className={styles.thresholdLine} />
                <text x={plot.left + chartWidth / 2} y={height - 7} textAnchor="middle" className={styles.axisLabel}>-log10(BUSTED p-value)</text>
                <text x={16} y={plot.top + chartHeight / 2} textAnchor="middle" transform={`rotate(-90 16 ${plot.top + chartHeight / 2})`} className={styles.axisLabel}>{countScale==="log"?"Genes (log1p scale)":"Genes"}</text>
            </svg>
        </FigureShell>
    );
}

BustedHistogram.propTypes = {
    genes: PropTypes.arrayOf(PropTypes.object).isRequired,
    thresholds: PropTypes.object.isRequired,
    onOpenGene: PropTypes.func.isRequired
};

const evidenceColor = (evidence) => {
    const count = Number(evidence.bustedFdr) + Number(evidence.absrel) + Number(evidence.meme);
    if (count === 3) return "#7a3fa0";
    if (count === 2) return "#146b8c";
    if (count === 1) return "#238636";
    return "#7d8995";
};

export function EvidenceScatterplot({ genes, thresholds, onOpenGene }) {
    const svgRef = useRef(null);
    const [hovered,setHovered]=useState(null);
    const points = useMemo(() => genes.flatMap((gene) => {
        if (gene.bustedPValue == null || !Number.isFinite(Number(gene.bustedPValue)) || gene.bustedPValue < 0 || gene.bustedPValue > 1) return [];
        const evidence = evaluateGeneEvidence(gene, thresholds);
        return [{
            gene,
            evidence,
            x: Math.min(12, -Math.log10(Math.max(Number(gene.bustedPValue), 1e-12))),
            y: evidence.selectedSites + evidence.selectedBranches
        }];
    }), [genes, thresholds]);
    const displayed = [...points]
        .sort((left, right) => right.y - left.y || right.x - left.x)
        ;
    const maxY = Math.max(1, ...displayed.map((point) => point.y));
    const width = 720;
    const height = 330;
    const plot = { left: 58, top: 22, right: 20, bottom: 52 };
    const chartWidth = width - plot.left - plot.right;
    const chartHeight = height - plot.top - plot.bottom;
    const projected=useMemo(()=>displayed.map(point=>({...point,cx:plot.left+point.x/12*chartWidth,cy:plot.top+chartHeight-point.y/maxY*chartHeight})),[points,maxY]);
    const paths=useMemo(()=>{
        const buckets=new Map();
        projected.forEach(point=>{const color=evidenceColor(point.evidence),r=point.evidence.any?5:3.5,x=(point.cx-r).toFixed(2),y=point.cy.toFixed(2);buckets.set(color,(buckets.get(color)||'')+`M${x},${y}a${r},${r} 0 1,0 ${2*r},0a${r},${r} 0 1,0 ${-2*r},0`);});
        return [...buckets].map(([color,d])=>({color,d}));
    },[projected]);
    const nearest=event=>{
        const rect=svgRef.current.getBoundingClientRect(),x=(event.clientX-rect.left)/rect.width*width,y=(event.clientY-rect.top)/rect.height*height;
        let best=null,distance=100;
        for(const point of projected){const d=(point.cx-x)**2+(point.cy-y)**2;if(d<distance){best=point;distance=d;}}
        return best;
    };
    const csv = rowsToDelimited(points.map((point) => ({
        gene: point.gene.displayName,
        orthogroup: point.gene.orthogroupId,
        bustedP: point.gene.bustedPValue,
        bustedQ: point.gene.bustedQValue,
        selectedBranches: point.evidence.selectedBranches,
        selectedSites: point.evidence.selectedSites
    })), [
        { label: "Gene", value: "gene" },
        { label: "Orthogroup", value: "orthogroup" },
        { label: "BUSTED p-value", value: "bustedP" },
        { label: "BUSTED BH q-value", value: "bustedQ" },
        { label: "aBSREL selected branches", value: "selectedBranches" },
        { label: "MEME selected sites", value: "selectedSites" }
    ]);

    return (
        <FigureShell
            title="Selection-evidence explorer"
            description="Gene-wide BUSTED evidence versus the number of selected branches and sites. Select a point to inspect that gene."
            count={points.length}
            filename="selection_evidence_scatterplot"
            svgRef={svgRef}
            csv={csv}
            note={points.length > displayed.length ? `The interactive plot shows the ${displayed.length} highest-evidence genes; the CSV includes all ${points.length}.` : null}
        >
            <svg ref={svgRef} className={styles.figureSvg} viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby="evidence-scatter-title evidence-scatter-desc" onPointerMove={event=>{const point=nearest(event);setHovered(old=>old?.gene.id===point?.gene.id?old:point);}} onPointerLeave={()=>setHovered(null)} onClick={event=>{const point=nearest(event);if(point)onOpenGene(point.gene);}}>
                <title id="evidence-scatter-title">Cross-method selection evidence scatterplot</title>
                <desc id="evidence-scatter-desc">Each point is a gene. The horizontal axis shows BUSTED evidence and the vertical axis shows selected aBSREL branches plus MEME sites.</desc>
                <line x1={plot.left} y1={plot.top + chartHeight} x2={width - plot.right} y2={plot.top + chartHeight} className={styles.axisLine} />
                <line x1={plot.left} y1={plot.top} x2={plot.left} y2={plot.top + chartHeight} className={styles.axisLine} />
                {[0, 3, 6, 9, 12].map((tick) => (
                    <g key={tick}>
                        <line x1={plot.left + tick / 12 * chartWidth} y1={plot.top} x2={plot.left + tick / 12 * chartWidth} y2={plot.top + chartHeight} className={styles.gridLine} />
                        <text x={plot.left + tick / 12 * chartWidth} y={height - 28} textAnchor="middle" className={styles.axisText}>{tick}</text>
                    </g>
                ))}
                {paths.map(path=><path key={path.color} d={path.d} fill={path.color} opacity="0.8"/>)}
                {hovered&&<circle cx={hovered.cx} cy={hovered.cy} r="7" fill="none" stroke="#172b46" strokeWidth="2"/>}
                <text x={plot.left + chartWidth / 2} y={height - 7} textAnchor="middle" className={styles.axisLabel}>-log10(BUSTED p-value)</text>
                <text x={16} y={plot.top + chartHeight / 2} textAnchor="middle" transform={`rotate(-90 16 ${plot.top + chartHeight / 2})`} className={styles.axisLabel}>Selected branches + sites</text>
            </svg>
            <p role="status">{hovered?`${hovered.gene.displayName}: BUSTED p=${formatNumber(hovered.gene.bustedPValue)}, q=${formatNumber(hovered.gene.bustedQValue)}; ${hovered.evidence.selectedBranches} branches, ${hovered.evidence.selectedSites} codons.`:"All readable genes are drawn. Use the gene table for keyboard access or overlapping points."}</p>
        </FigureShell>
    );
}

EvidenceScatterplot.propTypes = BustedHistogram.propTypes;

export function MethodOverlapPlot({ genes, thresholds, onChooseGenes }) {
    const svgRef = useRef(null);
    const combinations = useMemo(() => overlapCounts(genes, thresholds), [genes, thresholds]);
    const width = 720;
    const rowHeight = 38;
    const height = Math.max(180, 62 + combinations.length * rowHeight);
    const maxCount = Math.max(1, ...combinations.map((combination) => combination.count));
    const barStart = 245;
    const barWidth = width - barStart - 54;
    const csv = rowsToDelimited(combinations.map((combination) => ({
        combination: combination.key,
        count: combination.count,
        genes: combination.genes.map((gene) => gene.displayName)
    })), [
        { label: "Evidence combination", value: "combination" },
        { label: "Gene count", value: "count" },
        { label: "Genes", value: "genes" }
    ]);

    return (
        <FigureShell
            title="Cross-method overlap"
            description="UpSet-style counts of genes supported by each combination of methods. Select a row to apply it as the active gene subset."
            count={genes.length}
            filename="cross_method_overlap"
            svgRef={svgRef}
            csv={csv}
        >
            <svg ref={svgRef} className={styles.figureSvg} viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby="overlap-title overlap-desc">
                <title id="overlap-title">Cross-method overlap</title>
                <desc id="overlap-desc">Counts of genes with BUSTED, aBSREL, and MEME evidence combinations.</desc>
                {combinations.map((combination, index) => {
                    const y = 38 + index * rowHeight;
                    const active = new Set(combination.methods);
                    return (
                        <g key={combination.key} role="button" tabIndex="0" className={styles.overlapRow}
                            onClick={() => onChooseGenes(combination.genes)}
                            onKeyDown={(event) => {
                                if (event.key === "Enter" || event.key === " ") onChooseGenes(combination.genes);
                            }}>
                            <title>{`${combination.key}: ${combination.count} genes`}</title>
                            <text x="8" y={y + 5} className={styles.overlapLabel}>{combination.key}</text>
                            {["BUSTED", "aBSREL", "MEME"].map((method, methodIndex) => (
                                <circle key={method} cx={152 + methodIndex * 28} cy={y} r="7"
                                    className={active.has(method) ? styles.overlapDotActive : styles.overlapDot} />
                            ))}
                            <rect x={barStart} y={y - 10} width={combination.count / maxCount * barWidth} height="20" rx="2" className={styles.overlapBar} />
                            <text x={barStart + combination.count / maxCount * barWidth + 8} y={y + 5} className={styles.axisText}>{combination.count}</text>
                        </g>
                    );
                })}
                <text x="152" y="18" textAnchor="middle" className={styles.methodAxisLabel}>B</text>
                <text x="180" y="18" textAnchor="middle" className={styles.methodAxisLabel}>aB</text>
                <text x="208" y="18" textAnchor="middle" className={styles.methodAxisLabel}>M</text>
            </svg>
        </FigureShell>
    );
}

MethodOverlapPlot.propTypes = {
    genes: PropTypes.arrayOf(PropTypes.object).isRequired,
    thresholds: PropTypes.object.isRequired,
    onChooseGenes: PropTypes.func.isRequired
};

export function LineageHeatmap({ matrix, count, thresholds, onOpenGene }) {
    const svgRef = useRef(null);
    const rows = [...matrix.rows]
        .sort((left, right) => {
            const leftMinimum = Math.min(...left.values.map((value) => value.pValue));
            const rightMinimum = Math.min(...right.values.map((value) => value.pValue));
            return leftMinimum - rightMinimum;
        })
        .slice(0, 50);
    const species = matrix.species;
    const cell = Math.max(18, Math.min(30, Math.floor(600 / Math.max(1, species.length))));
    const labelWidth = 180;
    const width = Math.max(520, labelWidth + species.length * cell + 30);
    const height = Math.max(220, 110 + rows.length * 24);
    const valueMap = new Map(rows.flatMap((row) => row.values.map((value) => [`${row.gene.id}:${value.species}`, value])));
    const csvRows = rows.flatMap((row) => species.map((speciesName) => {
        const value = valueMap.get(`${row.gene.id}:${speciesName}`);
        return {
            gene: row.gene.displayName,
            orthogroup: row.gene.orthogroupId,
            species: speciesName,
            correctedP: value?.pValue ?? null,
            significant: value?.significant ?? false
        };
    }));
    const csv = rowsToDelimited(csvRows, [
        { label: "Gene", value: "gene" },
        { label: "Orthogroup", value: "orthogroup" },
        { label: "Terminal branch / species", value: "species" },
        { label: "aBSREL corrected p-value", value: "correctedP" },
        { label: "Significant", value: "significant" }
    ]);

    const color = (value) => {
        if (!value) return "#edf1f4";
        const score = Math.min(5, -Math.log10(Math.max(value.pValue, 1e-5))) / 5;
        const lightness = 88 - score * 48;
        return `hsl(167 55% ${lightness}%)`;
    };

    return (
        <FigureShell
            title="Lineage selection landscape"
            description="aBSREL corrected p-values on terminal branches that map unambiguously to a selected species."
            count={count}
            filename="absrel_lineage_selection_landscape"
            svgRef={svgRef}
            csv={csv}
            note={matrix.rows.length === 0
                ? "No aBSREL branch labels could be mapped reliably to species accessions. Internal gene-tree nodes are intentionally not aggregated."
                : `Showing ${rows.length} of ${matrix.rows.length} genes with mapped terminal branches; cells darker than the p <= ${thresholds.absrelP} threshold indicate stronger evidence.`}
        >
            <svg ref={svgRef} className={`${styles.figureSvg} ${styles.heatmapSvg}`} viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby="heatmap-title heatmap-desc">
                <title id="heatmap-title">aBSREL lineage selection heatmap</title>
                <desc id="heatmap-desc">Genes are rows and reliably mapped terminal species branches are columns. Darker cells have smaller corrected p-values.</desc>
                {species.map((speciesName, index) => {
                    const x = labelWidth + index * cell + cell / 2;
                    return <text key={speciesName} x={x} y="96" transform={`rotate(-55 ${x} 96)`} textAnchor="start" className={styles.heatmapLabel}>{speciesName}</text>;
                })}
                {rows.map((row, rowIndex) => {
                    const y = 104 + rowIndex * 24;
                    return (
                        <g key={row.gene.id} role="button" tabIndex="0" onClick={() => onOpenGene(row.gene)}
                            onKeyDown={(event) => {
                                if (event.key === "Enter" || event.key === " ") onOpenGene(row.gene);
                            }}>
                            <text x={labelWidth - 8} y={y + 15} textAnchor="end" className={styles.heatmapGeneLabel}>{row.gene.displayName}</text>
                            {species.map((speciesName, columnIndex) => {
                                const value = valueMap.get(`${row.gene.id}:${speciesName}`);
                                return (
                                    <rect key={speciesName} x={labelWidth + columnIndex * cell} y={y} width={cell - 2} height="20" rx="2" fill={color(value)}
                                        stroke={value?.significant ? "#0d5b4d" : "#d5dce3"} strokeWidth={value?.significant ? 2 : 1}>
                                        <title>{`${row.gene.displayName}, ${speciesName}: ${value ? `corrected p=${formatNumber(value.pValue)}` : "not mapped"}`}</title>
                                    </rect>
                                );
                            })}
                        </g>
                    );
                })}
            </svg>
        </FigureShell>
    );
}

LineageHeatmap.propTypes = {
    matrix: PropTypes.shape({ species: PropTypes.array, rows: PropTypes.array }).isRequired,
    count: PropTypes.number.isRequired,
    thresholds: PropTypes.object.isRequired,
    onOpenGene: PropTypes.func.isRequired
};

export function GoEnrichmentPlot({ results, backgroundCount, targetCount, onChooseGenes }) {
    const svgRef = useRef(null);
    const visible = results.filter((result) => Number(result.qValue) <= 0.05).slice(0, 20);
    const width = 720;
    const height = Math.max(220, 70 + visible.length * 30);
    const maximum = Math.max(1, ...visible.map((result) => result.foldEnrichment || 0));
    const csv = rowsToDelimited(results, [
        { label: "GO ID", value: "id" },
        { label: "Term", value: "name" },
        { label: "Namespace", value: "namespace" },
        { label: "Raw p-value", value: "pValue" },
        { label: "BH q-value", value: "qValue" },
        { label: "Fold enrichment", value: "foldEnrichment" },
        { label: "Selected gene count", value: "geneCount" },
        { label: "Background gene count", value: "backgroundCount" }
    ]);

    return (
        <FigureShell
            title="GO enrichment"
            description={`Over-representation among ${targetCount} evidence-positive genes using ${backgroundCount} tested genes as the background.`}
            count={backgroundCount}
            filename="go_enrichment"
            svgRef={svgRef}
            csv={csv}
            note={visible.length ? "Only terms with BH q <= 0.05 are plotted; the CSV contains every tested term." : "No GO terms pass BH q <= 0.05 for the current subset."}
        >
            <svg ref={svgRef} className={styles.figureSvg} viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby="go-title go-desc">
                <title id="go-title">Gene Ontology enrichment dot plot</title>
                <desc id="go-desc">Significantly enriched Gene Ontology terms. Dot position is fold enrichment and size is selected gene count.</desc>
                {visible.map((result, index) => {
                    const y = 36 + index * 30;
                    const x = 330 + (result.foldEnrichment / maximum) * 340;
                    return (
                        <g key={result.id} role="button" tabIndex="0" onClick={() => onChooseGenes(result.genes)}
                            onKeyDown={(event) => {
                                if (event.key === "Enter" || event.key === " ") onChooseGenes(result.genes);
                            }}>
                            <text x="8" y={y + 5} className={styles.goLabel}>{result.name}</text>
                            <circle cx={x} cy={y} r={Math.min(14, 4 + Math.sqrt(result.geneCount) * 2)} className={styles.goDot}>
                                <title>{`${result.id}: q=${formatNumber(result.qValue)}, ${formatNumber(result.foldEnrichment)}-fold, ${result.geneCount} genes`}</title>
                            </circle>
                        </g>
                    );
                })}
                {!visible.length && <text x={width / 2} y={height / 2} textAnchor="middle" className={styles.emptyChartText}>No significant enrichment to plot</text>}
                <text x="500" y={height - 10} textAnchor="middle" className={styles.axisLabel}>Fold enrichment</text>
            </svg>
        </FigureShell>
    );
}

GoEnrichmentPlot.propTypes = {
    results: PropTypes.arrayOf(PropTypes.object).isRequired,
    backgroundCount: PropTypes.number.isRequired,
    targetCount: PropTypes.number.isRequired,
    onChooseGenes: PropTypes.func.isRequired
};


export function RankedSelectionPlot({genes,thresholds,onOpenGene}) {
 const svg=useRef(null);const ranked=useMemo(()=>genes.filter(g=>g.bustedPValue!=null&&g.methodStatus.BUSTED==='complete').sort((a,b)=>a.bustedPValue-b.bustedPValue||a.id.localeCompare(b.id)).slice(0,30),[genes]);
 const score=p=>Math.min(16,-Math.log10(Math.max(p,1e-16))),maximum=Math.max(1,...ranked.map(g=>score(g.bustedPValue))),height=90+ranked.length*28;
 return <FigureShell title="Strongest gene-wide selection signals" description="The 30 strongest BUSTED results from the full displayed family. Green bars pass the across-gene BH threshold. Select a gene to open its detailed report." count={genes.filter(g=>g.methodStatus.BUSTED==='complete').length} filename="ranked_busted_genes" svgRef={svg} csv={rowsToDelimited(ranked,[{label:'Gene',value:'displayName'},{label:'Orthogroup',value:'orthogroupId'},{label:'p-value',value:'bustedPValue'},{label:'BH q',value:'bustedQValue'}])} note="Bar length is −log10(p), capped at 16 for display; tooltips and exports retain the original p-values. This plot ranks evidence, not evolutionary effect size.">
 <svg ref={svg} className={styles.figureSvg} viewBox={`0 0 800 ${height}`} role="img" aria-label="Top BUSTED genes ranked by selection evidence">{ranked.map((g,i)=><g key={g.id} role="button" tabIndex="0" onClick={()=>onOpenGene(g)} onKeyDown={e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();onOpenGene(g);}}}><title>{g.displayName}: p={g.bustedPValue}; BH q={g.bustedQValue}</title><text x="185" y={35+i*28} textAnchor="end" className={styles.axisText}>{g.displayName.length>24?g.displayName.slice(0,23)+'…':g.displayName}</text><rect x="195" y={23+i*28} height="18" width={score(g.bustedPValue)/maximum*520} fill={g.bustedQValue!=null&&g.bustedQValue<=thresholds.bustedQ?'#17796b':'#94a4b2'}/><text x={202+score(g.bustedPValue)/maximum*520} y={36+i*28} className={styles.axisText}>{score(g.bustedPValue).toFixed(1)}</text></g>)}<text x="440" y={height-15} textAnchor="middle" className={styles.axisLabel}>−log10(BUSTED p-value)</text></svg>
 </FigureShell>;
}
