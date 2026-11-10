import { API_BASES, apiUrl } from "./config";

const MAX_ERROR_DETAIL_CHARS = 1000;
const base = API_BASES.analysis;

function extractErrorDetail(json) {
  return json?.body ?? json?.diagnostics ?? json?.invocation ?? json?.raw;
}

function formatErrorDetail(detail) {
  if (detail === null || detail === undefined) return null;
  if (typeof detail === "string") {
    const trimmed = detail.trim();
    return trimmed.length > 0 ? trimmed : null;
  }
  try {
    const serialized = JSON.stringify(detail, null, 2);
    return serialized?.length ? serialized : null;
  } catch {
    return null;
  }
}

function buildError(msg, detail, status) {
  if (/<(?:!doctype|html|head|body)\b/i.test(`${msg} ${typeof detail === "string" ? detail : ""}`)) {
    msg = "Galaxy did not return a readable response. Retry loading the results; existing Galaxy data have not been changed.";
    detail = null;
  }
  const formatted = formatErrorDetail(detail);
  const error = new Error(formatted
    ? `${msg}\nDetails: ${formatted.slice(0, MAX_ERROR_DETAIL_CHARS)}`
    : msg);
  error.status = status;
  error.transient = status >= 500;
  return error;
}

async function parseResponse(resp) {
  const text = await resp.text();
  let json;
  try {
    json = text ? JSON.parse(text) : {};
  } catch {
    json = { raw: text };
  }

  if (!resp.ok || json?.success === false) {
    const msg = json?.error || json?.message || `HTTP ${resp.status}`;
    throw buildError(msg, extractErrorDetail(json), resp.status);
  }

  return json;
}

async function postJson(path, body, { timeoutMs = 60000, timeoutMessage } = {}) {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    const resp = await fetch(apiUrl(base, path), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    return await parseResponse(resp);
  } catch (error) {
    if (error?.name === "AbortError") {
      const timeoutError = new Error(
        timeoutMessage || "The request timed out. Please try again."
      );
      timeoutError.transient = true;
      throw timeoutError;
    }
    throw error;
  } finally {
    window.clearTimeout(timeout);
  }
}

export async function startDnDs() {
  throw new Error("The gene-based CDS candidate workflow has been removed. Use genome analysis instead.");
}

export async function startGenomeAnalysis({ species, ncbiApiKey, galaxyApiKey }) {
  return postJson("/api/dnds/genome-analysis/start", { species, ncbiApiKey, galaxyApiKey });
}

export async function validateGalaxyApiKey(galaxyApiKey) {
  return postJson("/api/dnds/galaxy/validate-key", { galaxyApiKey });
}

export async function resumeGenomeAnalysis({ historyId, orthoFinderJobId, galaxyApiKey }) {
  return postJson("/api/dnds/genome-analysis/attach", { historyId, orthoFinderJobId, galaxyApiKey }, { timeoutMs: 60000 });
}

export async function inspectGenomeAnalysis({ historyId, galaxyApiKey }) {
  return postJson("/api/dnds/genome-analysis/status", { historyId, galaxyApiKey });
}

export async function continuePreparedGenomeAnalysis({ historyId, galaxyApiKey }) {
  return postJson("/api/dnds/genome-analysis/continue-preparation", { historyId, galaxyApiKey });
}

export async function cancelGenomeAnalysis({ historyId, galaxyApiKey }) {
  return postJson("/api/dnds/genome-analysis/cancel", { historyId, galaxyApiKey });
}

export async function retryHyphyFromCompletedAlignments({ historyId, galaxyApiKey }) {
  return postJson("/api/dnds/genome-analysis/retry-hyphy", { historyId, galaxyApiKey }, {
    timeoutMs: 920000,
    timeoutMessage: "The recovery request has not returned. It may still be processing on the local API; check history status before submitting again."
  });
}

export function subscribeToGenomeAnalysisProgress(sessionId, { onProgress, onComplete, onError } = {}) {
  const eventSource = new EventSource(
    apiUrl(base, `/api/dnds/genome-analysis/progress/${encodeURIComponent(sessionId)}`)
  );
  let receivedProgress = false;

  eventSource.onmessage = (event) => {
    try {
      if (event.data.startsWith(":")) return;
      const update = JSON.parse(event.data);
      receivedProgress = true;
      onProgress?.(update);
    } catch (e) {
      onError?.(e);
    }
  };

  eventSource.onerror = (error) => {
    if (eventSource.readyState === EventSource.CLOSED || receivedProgress) {
      onComplete?.();
    } else {
      onError?.(error);
    }
    eventSource.close();
  };

  return () => eventSource.close();
}

function galaxyHeaders(galaxyApiKey) {
  return galaxyApiKey ? { "X-Galaxy-Api-Key": galaxyApiKey } : {};
}

export async function getOrthologousGroups(historyId, orthoFinderJobId, galaxyApiKey) {
  const resp = await fetch(
    apiUrl(base, `/api/dnds/orthologous-groups/${encodeURIComponent(historyId)}/${encodeURIComponent(orthoFinderJobId)}`),
    { cache: "no-store", headers: galaxyHeaders(galaxyApiKey) }
  );
  const json = await parseResponse(resp);
  return json?.groups || [];
}

export async function getHyphyOutputs(historyId, galaxyApiKey) {
  const resp = await fetch(
    apiUrl(base, `/api/dnds/genome-analysis/${encodeURIComponent(historyId)}/hyphy-outputs`),
    { cache: "no-store", headers: galaxyHeaders(galaxyApiKey) }
  );
  return parseResponse(resp);
}

export async function getOrthogroupAnnotations(accessions, ncbiApiKey) {
  const json = await postJson("/api/dnds/orthologous-groups/annotations", { accessions, ncbiApiKey });
  return json?.annotations || {};
}

export async function downloadOrthoFinderOutput(datasetId, options = {}) {
  const galaxyApiKey = typeof options === "string" ? options : options?.galaxyApiKey;
  for (let attempt = 0; attempt < 3; attempt += 1) {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 45000);
  try {
  const resp = await fetch(
    apiUrl(base, `/api/dnds/orthofinder/output/${encodeURIComponent(datasetId)}/download`),
    { cache: "no-store", headers: galaxyHeaders(galaxyApiKey), signal: controller.signal }
  );
  if (!resp.ok) {
    const text = await resp.text();
    throw buildError(`Galaxy output could not be retrieved (HTTP ${resp.status}).`, text, resp.status);
  }
  const text = await resp.text();
  if (/^\s*<(?:!doctype|html)\b/i.test(text)) throw buildError("Galaxy returned HTML instead of a result.", text, 502);
  return text;
  } catch (error) {
    const transient = error.transient || error.name === "AbortError" || error instanceof TypeError;
    if (!transient || attempt === 2) throw error.name === "AbortError" ? buildError("Galaxy output retrieval timed out. Retry loading results.", null, 504) : error;
  } finally { window.clearTimeout(timeout); }
  await new Promise(resolve => window.setTimeout(resolve, 750 * (2 ** attempt)));
  }
}

export async function analyzeOrthologousGroup({ historyId, orthoGroup, genomes, galaxyApiKey, ncbiApiKey, alignment }) {
  return postJson("/api/dnds/orthologous-group/analyze", { historyId, orthoGroup, genomes, galaxyApiKey, ncbiApiKey, alignment });
}

export async function analyzeMultipleGroups({ historyId, orthoGroups, genomes, galaxyApiKey, ncbiApiKey, alignment }) {
  return postJson("/api/dnds/orthologous-groups/analyze-multiple", { historyId, orthoGroups, genomes, galaxyApiKey, ncbiApiKey, alignment });
}

export async function analyzeAllGroups({ historyId, orthoFinderJobId, genomes, galaxyApiKey, ncbiApiKey, alignment }) {
  return postJson("/api/dnds/orthologous-groups/analyze-all", { historyId, orthoFinderJobId, genomes, galaxyApiKey, ncbiApiKey, alignment });
}

export async function getCandidates() {
  throw new Error("The gene-based CDS candidate workflow has been removed. Use genome analysis instead.");
}

export async function suggestSpecies({ query, ncbiApiKey, limit = 25 }) {
  const url = new URL(apiUrl(base, "/api/dnds/species/suggest"));
  url.searchParams.set("query", query);
  url.searchParams.set("limit", String(limit));
  if (ncbiApiKey) url.searchParams.set("apiKey", ncbiApiKey);

  const resp = await fetch(url.toString(), { cache: "no-store" });
  const json = await parseResponse(resp);
  const suggestions = Array.isArray(json?.suggestions) ? json.suggestions : [];
  return suggestions.map((item) => ({
    scientific: item.scientific ?? item.Scientific ?? "",
    common: item.common ?? item.Common ?? "",
    taxId: item.taxId ?? item.TaxId ?? "",
    accession: item.accession ?? item.Accession ?? "",
    assemblyName: item.assemblyName ?? item.AssemblyName ?? "",
    assemblyLevel: item.assemblyLevel ?? item.AssemblyLevel ?? "",
    isReference: item.isReference ?? item.IsReference ?? false,
    hasAnnotation: item.hasAnnotation ?? item.HasAnnotation ?? false,
    annotationName: item.annotationName ?? item.AnnotationName ?? "",
    scaffoldN50: item.scaffoldN50 ?? item.ScaffoldN50 ?? null,
    contigN50: item.contigN50 ?? item.ContigN50 ?? null,
    buscoComplete: item.buscoComplete ?? item.BuscoComplete ?? null,
    submitter: item.submitter ?? item.Submitter ?? "",
    submissionDate: item.submissionDate ?? item.SubmissionDate ?? "",
    releaseDate: item.releaseDate ?? item.ReleaseDate ?? "",
    bioprojectAccession: item.bioProjectAccession ?? item.BioProjectAccession ?? "",
    bioprojectTitle: item.bioProjectTitle ?? item.BioProjectTitle ?? "",
    description: item.description ?? item.Description ?? "",
  })).filter((item) => item.scientific);
}

export async function getAssemblyPublicationDetails(accession, ncbiApiKey) {
  const url = new URL(apiUrl(base, "/api/dnds/species/details"));
  url.searchParams.set("accession", accession);
  if (ncbiApiKey) url.searchParams.set("apiKey", ncbiApiKey);
  const response = await fetch(url.toString(), { cache: "no-store" });
  const json = await parseResponse(response);
  return json?.details || null;
}

export async function getReferenceSpeciesTree(species, ncbiApiKey) {
  const response = await postJson("/api/dnds/species/tree", { species, ncbiApiKey }, {
    timeoutMs: 180000,
    timeoutMessage: "NCBI did not return the context tree within three minutes. Please try again."
  });
  return response?.tree || null;
}

export async function getSpeciesTreeTimes(newick, leafTaxIds) {
  const response = await postJson("/api/dnds/species/tree/times", { newick, leafTaxIds });
  return response?.divergenceTimes || [];
}

export async function getJob(jobId, galaxyApiKey) {
  const headers = {};
  if (galaxyApiKey) headers["X-Galaxy-Api-Key"] = galaxyApiKey;
  const resp = await fetch(apiUrl(base, `/api/dnds/job/${encodeURIComponent(jobId)}`), {
    cache: "no-store",
    headers,
  });
  return parseResponse(resp);
}

export async function getOrthoFinderOutputs(historyId, orthoFinderJobId, galaxyApiKey) {
  const resp = await fetch(
    apiUrl(base, `/api/dnds/orthofinder/outputs/${encodeURIComponent(historyId)}/${encodeURIComponent(orthoFinderJobId)}`),
    { cache: "no-store", headers: galaxyHeaders(galaxyApiKey) }
  );
  return parseResponse(resp);
}

export async function getHyphyMarkdownTable(historyId, datasetId, galaxyApiKey) {
  const resp = await fetch(
    apiUrl(base, `/api/dnds/hyphy/markdown/${encodeURIComponent(historyId)}/${encodeURIComponent(datasetId)}`),
    { cache: "no-store", headers: galaxyHeaders(galaxyApiKey) }
  );
  const json = await parseResponse(resp);
  return json?.data || [];
}

export async function getAnalysisResultArtifacts(historyId, galaxyApiKey) {
  const resp = await fetch(
    apiUrl(base, `/api/dnds/genome-analysis/${encodeURIComponent(historyId)}/result-artifacts`),
    { cache: "no-store", headers: galaxyHeaders(galaxyApiKey) }
  );
  const json = await parseResponse(resp);
  return Array.isArray(json?.artifacts) ? json.artifacts : [];
}

export async function downloadOrthoFinderOutputFile(datasetId, filename, galaxyApiKey) {
  const params = new URLSearchParams();
  if (filename) params.append("filename", filename);
  const qs = params.toString() ? `?${params.toString()}` : "";
  const resp = await fetch(
    apiUrl(base, `/api/dnds/orthofinder/output/${encodeURIComponent(datasetId)}/download${qs}`),
    { headers: galaxyHeaders(galaxyApiKey) }
  );
  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(text || `HTTP ${resp.status}`);
  }
  const blob = await resp.blob();
  const downloadName = filename || datasetId;
  const blobUrl = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = blobUrl;
  a.download = downloadName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.URL.revokeObjectURL(blobUrl);
}

export async function downloadOrthoFinderOutputZip(datasetIds, filename, galaxyApiKey, options = {}) {
  const datasets = Array.isArray(options?.datasets) ? options.datasets : [];
  const generatedFiles = Array.isArray(options?.generatedFiles) ? options.generatedFiles : [];
  const resp = await fetch(apiUrl(base, "/api/dnds/orthofinder/output/zip"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ datasetIds, datasets, generatedFiles, filename, galaxyApiKey }),
  });
  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(text || `HTTP ${resp.status}`);
  }
  const blob = await resp.blob();
  const downloadName = filename || "outputs.zip";
  const blobUrl = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = blobUrl;
  a.download = downloadName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.URL.revokeObjectURL(blobUrl);
}

export async function getOrthogroupGenes(historyId, orthogroupId, galaxyApiKey) {
  const resp = await fetch(
    apiUrl(base, `/api/dnds/orthogroup/${encodeURIComponent(historyId)}/${encodeURIComponent(orthogroupId)}/genes`),
    { cache: "no-store", headers: galaxyHeaders(galaxyApiKey) }
  );
  const json = await parseResponse(resp);
  return json?.speciesGenes || json?.sequences || {};
}

export function pollJob(jobId, { intervalMs = 4000, galaxyApiKey, onTick, onError } = {}) {
  const timer = setInterval(async () => {
    try {
      const job = await getJob(jobId, galaxyApiKey);
      onTick?.(job);
      const state = job?.state;
      if (state && ["ok", "error", "failed"].includes(state)) {
        clearInterval(timer);
      }
    } catch (e) {
      onError?.(e);
      clearInterval(timer);
    }
  }, intervalMs);
  return () => clearInterval(timer);
}

export async function verifyGenome({ species, ncbiApiKey }) {
  const url = new URL(apiUrl(base, "/api/dnds/verify-genome"));
  url.searchParams.set("species", species);
  if (ncbiApiKey) url.searchParams.set("apiKey", ncbiApiKey);
  const resp = await fetch(url.toString(), { cache: "no-store" });
  const json = await parseResponse(resp);
  return { found: !!json?.found, genomeUrl: json?.genomeUrl || null };
}
