const cache = new Map();
const origin = "https://api.phylopic.org";

async function read(url) {
    const response = await fetch(new URL(url, origin), { signal: AbortSignal.timeout(12000) });
    if (!response.ok) throw new Error("PhyloPic is unavailable");
    return response.json();
}

export function getSpeciesSilhouette(species) {
    const name = String(species).replace(/_/g, " ").trim();
    if (cache.has(name)) return cache.get(name);
    const request = (async () => {
        for (const query of [...new Set([name, name.split(" ")[0]])]) {
            const search = await read(`/nodes?filter_name=${encodeURIComponent(query.toLowerCase())}`);
            if (!search._links?.firstPage?.href) continue;
            const page = await read(search._links.firstPage.href);
            for (const item of (page._links?.items || []).slice(0, 1)) {
                const node = await read(item.href);
                const matches = (node.names || []).some((parts) => parts.some((part) =>
                    part.class === "scientific" && part.text.toLowerCase() === query.toLowerCase()));
                if (!matches) continue;
                if (!node._links?.primaryImage?.href) continue;
                const image = await read(node._links.primaryImage.href);
                const license = image._links?.license?.href || "";
                // Keep reuse straightforward for exported scientific figures.
                if (!/creativecommons.org\/(publicdomain|licenses\/by\/)/.test(license)) continue;
                const asset = image._links?.thumbnailFiles?.[0]?.href;
                if (!asset?.startsWith("https://images.phylopic.org/")) continue;
                return { url: asset, credit: image.attribution || image._links?.contributor?.title || "Unknown contributor",
                    license, source: `https://www.phylopic.org/images/${image.uuid}`,
                    taxon: item.title || query, fallback: query !== name };
            }
        }
        return null;
    })();
    cache.set(name, request);
    request.catch(() => cache.delete(name));
    return request;
}
