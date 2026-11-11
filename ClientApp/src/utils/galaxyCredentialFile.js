// Parse only explicitly labelled values. Never log or persist this text.
export function parseGalaxyCredentialFile(text) {
    if (typeof text !== 'string' || text.length > 65536) throw new Error('Use a small text or RTF file with labelled Key and History ID fields.');
    const plain = text.replace(/\\[a-zA-Z]+-?\d* ?/g, ' ').replace(/[{}]/g, ' ');
    const key = plain.match(/(?:Galaxy\s+API\s+Key|API\s+Key|Key)\s*:\s*([a-f0-9]{32})\b/i)?.[1];
    const historyId = plain.match(/(?:Completed\s+Analysis\s+ID(?:\s*\([^)]*\))?|(?:Galaxy\s+)?History\s+ID)\s*:\s*([a-f0-9]{32})\b/i)?.[1];
    if (!key || !historyId) throw new Error('File must contain labelled Key: and History ID: values. No analysis was submitted.');
    return { key, historyId };
}
