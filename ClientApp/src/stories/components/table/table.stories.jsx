import { MemoryRouter } from 'react-router-dom';
import ResultsTable from '../../../Components/Table/ResultsTable/ResultsTable';

export default {
  title: 'Components/ResultsTable',
  component: ResultsTable,
  decorators: [
    (Story) => (
      <MemoryRouter>
        <div style={{ padding: '2rem' }}>
          <Story />
        </div>
      </MemoryRouter>
    ),
  ],
  argTypes: {
    rows: {
      control: 'object',
      description: 'Array of row data objects',
    },
    headers: {
      control: 'object',
      description: 'Array of main header objects with id, label, colspan',
    },
    subHeaders: {
      control: 'object',
      description: 'Array of subheader objects with parent and labels',
    },
    includeSubHeader: {
      control: 'boolean',
      description: 'Whether to show the subheader row',
    },
    expandedContent: {
      control: false,
      description: 'Function or component to render in expanded rows',
    },
  },
};

// Sample data
const sampleHeaders = [
  { id: 1, label: 'Organism', colspan: 2 },
  { id: 2, label: 'Assembly', colspan: 2 },
  { id: 3, label: 'Gene Count', colspan: 1 },
];

const sampleSubHeaders = [
  { parent: 'Organism', labels: ['Scientific Name', 'Common Name'] },
  { parent: 'Assembly', labels: ['Assembly Name', 'Level'] },
  { parent: 'Gene Count', labels: ['Count'] },
];

const sampleRows = [
  {
    organism: {
      scientific: 'Homo sapiens',
      common: 'Human',
    },
    assembly: {
      name: 'GRCh38.p14',
      level: 'Chromosome',
    },
    geneCount: '20,000',
    _fullData: { id: 1, details: 'Full data object' },
    _saveAction: () => Promise.resolve(),
    _downloadAction: (data) => console.log('Download:', data),
  },
  {
    organism: {
      scientific: 'Mus musculus',
      common: 'Mouse',
    },
    assembly: {
      name: 'GRCm39',
      level: 'Chromosome',
    },
    geneCount: '22,000',
    _fullData: { id: 2, details: 'Full data object' },
    _saveAction: () => Promise.resolve(),
    _downloadAction: (data) => console.log('Download:', data),
  },
  {
    organism: {
      scientific: 'Drosophila melanogaster',
      common: 'Fruit fly',
    },
    assembly: {
      name: 'BDGP6.32',
      level: 'Chromosome',
    },
    geneCount: '14,000',
    _fullData: { id: 3, details: 'Full data object' },
    _saveAction: () => Promise.resolve(),
    _downloadAction: (data) => console.log('Download:', data),
  },
];

// Basic Table with SubHeaders
export const Default = {
  args: {
    headers: sampleHeaders,
    subHeaders: sampleSubHeaders,
    rows: sampleRows,
    includeSubHeader: true,
  },
};

// Without SubHeaders
export const WithoutSubHeaders = {
  args: {
    headers: sampleHeaders,
    rows: sampleRows,
    includeSubHeader: false,
  },
};

// Simple Table (fewer columns)
export const SimpleTable = {
  args: {
    headers: [
      { id: 1, label: 'Species', colspan: 1 },
      { id: 2, label: 'Count', colspan: 1 },
    ],
    rows: [
      {
        species: 'Homo sapiens',
        count: '20,000',
        _fullData: { id: 1 },
        _saveAction: () => Promise.resolve(),
        _downloadAction: (data) => console.log('Download:', data),
      },
      {
        species: 'Mus musculus',
        count: '22,000',
        _fullData: { id: 2 },
        _saveAction: () => Promise.resolve(),
        _downloadAction: (data) => console.log('Download:', data),
      },
    ],
    includeSubHeader: false,
  },
};

// Large Dataset
export const LargeDataset = {
  args: {
    headers: sampleHeaders,
    subHeaders: sampleSubHeaders,
    rows: Array.from({ length: 10 }, (_, i) => ({
      organism: {
        scientific: `Species ${i + 1}`,
        common: `Common name ${i + 1}`,
      },
      assembly: {
        name: `Assembly ${i + 1}`,
        level: i % 2 === 0 ? 'Chromosome' : 'Scaffold',
      },
      geneCount: `${15000 + i * 1000}`,
      _fullData: { id: i + 1 },
      _saveAction: () => Promise.resolve(),
      _downloadAction: (data) => console.log('Download:', data),
    })),
    includeSubHeader: true,
  },
};

// With Expanded Content
export const WithExpandedContent = {
  args: {
    headers: sampleHeaders,
    subHeaders: sampleSubHeaders,
    rows: sampleRows,
    includeSubHeader: true,
    expandedContent: (rowData) => (
      <div style={{ padding: '1rem' }}>
        <h4 style={{ marginTop: 0 }}>Detailed Information</h4>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
          <div>
            <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Organism Details:</p>
            <ul style={{ margin: 0, paddingLeft: '1.5rem' }}>
              <li>Scientific: {rowData.organism?.scientific}</li>
              <li>Common: {rowData.organism?.common}</li>
            </ul>
          </div>
          <div>
            <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Assembly Details:</p>
            <ul style={{ margin: 0, paddingLeft: '1.5rem' }}>
              <li>Name: {rowData.assembly?.name}</li>
              <li>Level: {rowData.assembly?.level}</li>
            </ul>
          </div>
        </div>
        <p style={{ marginTop: '1rem', color: '#666', fontSize: '0.875rem' }}>
          Gene Count: <strong>{rowData.geneCount}</strong>
        </p>
      </div>
    ),
  },
};

// Different Column Structures
export const DifferentColumnStructures = {
  render: () => {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: '3rem' }}>
        <div>
          <h3 style={{ marginBottom: '1rem' }}>2 Main Headers</h3>
          <ResultsTable
            headers={[
              { id: 1, label: 'Organism Info', colspan: 2 },
              { id: 2, label: 'Data', colspan: 1 },
            ]}
            subHeaders={[
              { parent: 'Organism Info', labels: ['Name', 'Type'] },
              { parent: 'Data', labels: ['Count'] },
            ]}
            rows={[
              {
                organism: { name: 'Species A', type: 'Mammal' },
                count: '100',
                _fullData: {},
                _saveAction: () => Promise.resolve(),
                _downloadAction: () => {},
              },
            ]}
            includeSubHeader={true}
          />
        </div>

        <div>
          <h3 style={{ marginBottom: '1rem' }}>4 Main Headers</h3>
          <ResultsTable
            headers={[
              { id: 1, label: 'A', colspan: 1 },
              { id: 2, label: 'B', colspan: 1 },
              { id: 3, label: 'C', colspan: 1 },
              { id: 4, label: 'D', colspan: 1 },
            ]}
            rows={[
              {
                a: 'Value A',
                b: 'Value B',
                c: 'Value C',
                d: 'Value D',
                _fullData: {},
                _saveAction: () => Promise.resolve(),
                _downloadAction: () => {},
              },
            ]}
            includeSubHeader={false}
          />
        </div>
      </div>
    );
  },
};

// Hover Effects Demo
export const HoverEffectsDemo = {
  args: {
    headers: sampleHeaders,
    subHeaders: sampleSubHeaders,
    rows: sampleRows,
    includeSubHeader: true,
  },
  render: (args) => (
    <div>
      <p style={{ marginBottom: '1rem', fontWeight: '600' }}>
        Hover over rows to see the background change:
      </p>
      <ResultsTable {...args} />
      <div
        style={{
          marginTop: '2rem',
          padding: '1rem',
          background: '#f5f5f5',
          borderRadius: '8px',
        }}
      >
        <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Hover effects:</p>
        <ul style={{ paddingLeft: '1.5rem', lineHeight: '1.8', margin: 0 }}>
          <li>Row background changes to #f9f7f7</li>
          <li>Three-dot menu button shows hover effect</li>
          <li>Smooth transition on hover</li>
        </ul>
      </div>
    </div>
  ),
};

// Interactive Features
export const InteractiveFeatures = {
  render: () => {
    return (
      <div>
        <p style={{ marginBottom: '1rem', fontWeight: '600' }}>
          Try these interactions:
        </p>
        <ResultsTable
          headers={sampleHeaders}
          subHeaders={sampleSubHeaders}
          rows={sampleRows}
          includeSubHeader={true}
          expandedContent={(rowData) => (
            <div style={{ padding: '1rem', background: '#f5f5f5' }}>
              <p style={{ margin: 0 }}>
                Expanded content for {rowData.organism?.scientific}
              </p>
            </div>
          )}
        />
        <div
          style={{
            marginTop: '2rem',
            padding: '1rem',
            background: '#f5f5f5',
            borderRadius: '8px',
          }}
        >
          <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>
            Available interactions:
          </p>
          <ul style={{ paddingLeft: '1.5rem', lineHeight: '1.8', margin: 0 }}>
            <li>Click three-dot menu for options</li>
            <li>Click "Show More Information" to expand rows</li>
            <li>Click "Save Search Query" (requires login)</li>
            <li>Click "Download Data"</li>
            <li>Click "Search For Genes" to navigate</li>
          </ul>
        </div>
      </div>
    );
  },
};