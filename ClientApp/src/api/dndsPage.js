import { startDnDs, pollJob, getCandidates } from './dndsApi.js';

(function init() {
  const geneInput = document.getElementById('gene-input');
  const speciesInput = document.getElementById('species-input');
  const addBtn = document.getElementById('add-species-btn');
  const speciesList = document.getElementById('species-list');
  const startBtn = document.getElementById('start-btn');
  const progressBar = document.getElementById('job-progress');
  const progressText = document.getElementById('job-status');
  const alignSelect = document.getElementById('align-select');

  const selected = [];
  const chosenIds = {}; // species -> uid

  const slug = (s) => (s || '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

  function setStatus(txt, pct) {
    progressText.textContent = txt;
    if (typeof pct === 'number') {
      progressBar.style.width = `${pct}%`;
      progressBar.setAttribute('aria-valuenow', pct);
      progressBar.textContent = `${pct}%`;
    }
  }

    async function esearchSpecies(species) {
        const base = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esearch.fcgi";
        const params = new URLSearchParams({
            db: "nuccore",
            retmode: "json",
            retmax: "1",
            term: `${species}[Organism] AND (cds)`
        });

        const url = `${base}?${params.toString()}`;
        console.log("Verifying species URL:", url); 
        const resp = await fetch(url, { cache: "no-store" });
        if (!resp.ok) return false;
        const data = await resp.json();
        const count = parseInt(data?.esearchresult?.count || "0", 10);
        return count > 0;
    }


  let geneDebounce;
  function scheduleRenderCandidates() {
    clearTimeout(geneDebounce);
    geneDebounce = setTimeout(renderCandidates, 300);
  }

  async function renderCandidates() {
    const gene = (geneInput.value || '').trim();
    selected.forEach(sp => {
      const container = document.getElementById(`cand-${slug(sp)}`);
      if (!container) return;
      container.innerHTML = '';
      if (!gene) {
        container.textContent = 'Enter a gene to load CDS options';
      } else {
        container.textContent = 'Loading…';
      }
    });

    if (!gene || selected.length === 0) return;
    try {
      const map = await getCandidates({ gene, species: selected });
      selected.forEach(sp => {
        const container = document.getElementById(`cand-${slug(sp)}`);
        if (!container) return;
        container.innerHTML = '';
        const list = map[sp] || [];
        if (list.length === 0) {
          container.textContent = 'No CDS candidates found';
          return;
        }
        const sel = document.createElement('select');
        sel.className = 'form-select form-select-sm';
        list.forEach((c, idx) => {
          const opt = document.createElement('option');
          opt.value = c.uid;
          opt.textContent = `${c.accession} • ${c.length} nt • ${c.title}`;
          if (idx === 0 && !chosenIds[sp]) chosenIds[sp] = c.uid;
          if (chosenIds[sp] === c.uid) opt.selected = true;
          sel.appendChild(opt);
        });
        sel.addEventListener('change', () => { chosenIds[sp] = sel.value; });
        container.appendChild(sel);
      });
    } catch (e) {
      console.error(e);
    }
  }

  function renderList() {
    speciesList.innerHTML = '';
    selected.forEach((sp, idx) => {
      const li = document.createElement('li');
      li.className = 'list-group-item';

      const row = document.createElement('div');
      row.className = 'd-flex align-items-center justify-content-between gap-2';

      const name = document.createElement('div');
      name.textContent = sp;
      row.appendChild(name);

      const cand = document.createElement('div');
      cand.id = `cand-${slug(sp)}`;
      cand.textContent = 'Enter a gene to load CDS options';
      row.appendChild(cand);

      const rm = document.createElement('button');
      rm.className = 'btn btn-sm btn-outline-danger';
      rm.textContent = 'Remove';
      rm.addEventListener('click', () => { selected.splice(idx, 1); delete chosenIds[sp]; renderList(); scheduleRenderCandidates(); });
      row.appendChild(rm);

      li.appendChild(row);
      speciesList.appendChild(li);
    });

    // After rendering placeholders, fetch candidates if possible
    scheduleRenderCandidates();
  }

  addBtn.addEventListener('click', async () => {
    const sp = (speciesInput.value || '').trim();
    if (!sp) return;
    setStatus(`Verifying ${sp}…`, undefined);
    try {
      const ok = await esearchSpecies(sp);
      if (!ok) {
        setStatus(`Species not found in NCBI: ${sp}`, undefined);
        return;
      }
      if (!selected.includes(sp)) selected.push(sp);
      speciesInput.value = '';
      renderList();
      setStatus('Species added', undefined);
    } catch (e) {
      setStatus(`Error verifying species: ${e?.message || e}`, undefined);
    }
  });

  // Auto-refresh candidate lists when the gene changes
  geneInput.addEventListener('input', scheduleRenderCandidates);
  geneInput.addEventListener('change', scheduleRenderCandidates);

  startBtn.addEventListener('click', async () => {
    const gene = geneInput.value?.trim();
    if (!gene || selected.length < 3) {
      alert('Enter a gene and add at least 3 species.');
      return;
    }
    const alignment = alignSelect?.value || 'mafft';
    setStatus('Starting pipeline…', 5);
    try {
      const selectedIds = {};
      selected.forEach(sp => { if (chosenIds[sp]) selectedIds[sp] = chosenIds[sp]; });

      const payload = {
        gene: String(gene),
        species: selected.slice().map(s => String(s)).filter(Boolean),
        alignment: String(alignment),
        selectedIds
      };

      const resp = await startDnDs(payload);

      let jobId = resp?.jobId ?? resp?.id ?? resp?.JobId ?? resp?.jobID;
      if (!jobId) {
        throw new Error('Invalid API response: missing jobId.');
      }

      setStatus('BUSTED submitted. Polling…', 20);
      pollJob(jobId, {
        intervalMs: 6000,
        onTick: (job) => {
          const state = job?.state || 'unknown';
          let pct = 25;
          if (state === 'new' || state === 'queued') pct = 25;
          else if (state === 'running') pct = 60;
          else if (state === 'ok') pct = 100;
          else if (state === 'error' || state === 'failed') pct = 100;
          setStatus(`Galaxy job: ${state}`, pct);
        },
        onError: (e) => setStatus(`Error polling job: ${e?.message || e}`, 100)
      });
    } catch (e) {
      console.error('startDnDs error:', e);
      const msg = e?.message || e?.toString?.() || 'Unknown error';
      setStatus(`Error starting pipeline: ${msg}`, 0);
    }
  });
})();
