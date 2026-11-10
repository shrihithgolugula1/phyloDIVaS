import { useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import ThreeDotMenu from '../../../Components/ThreeDots/ThreeDotMenu';

export default {
  title: 'Components/ThreeDotMenu',
  component: ThreeDotMenu,
  decorators: [
    (Story) => (
      <MemoryRouter>
        <div style={{ padding: '2rem', minHeight: '400px' }}>
          <Story />
        </div>
      </MemoryRouter>
    ),
  ],
  argTypes: {
    rowIndex: {
      control: 'number',
      description: 'Index of the current row',
    },
    expandedRow: {
      control: 'number',
      description: 'Currently expanded row index (or null)',
    },
    onShowMore: {
      action: 'show more clicked',
      description: 'Callback when show more is clicked',
    },
    onSaveSearch: {
      action: 'save search clicked',
      description: 'Callback when save search is clicked',
    },
    onDownload: {
      action: 'download clicked',
      description: 'Callback when download is clicked',
    },
    setExpandedRow: {
      action: 'expanded row changed',
      description: 'Callback to set the expanded row',
    },
  },
};

// Default Menu
export const Default = {
  render: () => {
    const [expandedRow, setExpandedRow] = useState(null);

    return (
      <div>
        <p style={{ marginBottom: '1rem', fontWeight: '600' }}>
          Click the three dots to open the menu:
        </p>
        <ThreeDotMenu
          rowIndex={0}
          expandedRow={expandedRow}
          setExpandedRow={setExpandedRow}
          onShowMore={() => console.log('Show more clicked')}
          onSaveSearch={() => Promise.resolve()}
          onDownload={() => console.log('Download clicked')}
        />
      </div>
    );
  },
};


// With Row Expansion
export const WithRowExpansion = {
  render: () => {
    const [expandedRow, setExpandedRow] = useState(null);

    return (
      <div>
        <div style={{ marginBottom: '2rem' }}>
          <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>
            Multiple Rows with Expansion
          </p>
          <p style={{ color: '#666', fontSize: '0.875rem', marginBottom: '1rem' }}>
            Click "Show More Information" to expand/collapse rows
          </p>
        </div>

        {[0, 1, 2].map((index) => (
          <div
            key={index}
            style={{
              padding: '1rem',
              marginBottom: '0.5rem',
              border: '1px solid #ddd',
              borderRadius: '8px',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'flex-start',
              background: expandedRow === index ? '#f5f5f5' : 'white',
            }}
          >
            <div style={{ flex: 1 }}>
              <h4 style={{ margin: '0 0 0.5rem 0' }}>Row {index + 1}</h4>
              <p style={{ margin: 0, color: '#666' }}>
                Basic information for row {index + 1}
              </p>
              {expandedRow === index && (
                <div
                  style={{
                    marginTop: '1rem',
                    padding: '1rem',
                    background: 'white',
                    borderRadius: '4px',
                    border: '1px solid #e0e0e0',
                  }}
                >
                  <p style={{ margin: 0, fontWeight: '600' }}>
                    Extended information:
                  </p>
                  <ul style={{ marginTop: '0.5rem', paddingLeft: '1.5rem' }}>
                    <li>Additional detail 1</li>
                    <li>Additional detail 2</li>
                    <li>Additional detail 3</li>
                  </ul>
                </div>
              )}
            </div>
            <ThreeDotMenu
              rowIndex={index}
              expandedRow={expandedRow}
              setExpandedRow={setExpandedRow}
              onShowMore={() => {}}
              onSaveSearch={() => Promise.resolve()}
              onDownload={() => console.log(`Download row ${index}`)}
            />
          </div>
        ))}
      </div>
    );
  },
};

// Save Search Flow (Not Logged In)
export const SaveSearchNotLoggedIn = {
  render: () => {
    const [expandedRow, setExpandedRow] = useState(null);

    // Clear session storage to simulate not logged in
    sessionStorage.removeItem('idToken');

    return (
      <div>
        <div
          style={{
            padding: '1rem',
            background: '#fff3cd',
            border: '1px solid #ffc107',
            borderRadius: '8px',
            marginBottom: '1rem',
          }}
        >
          <p style={{ margin: 0, fontWeight: '600', color: '#856404' }}>
            ⚠️ Simulating not logged in (no idToken)
          </p>
        </div>
        <p style={{ marginBottom: '1rem' }}>
          Click the menu and select "Save Search Query" to see the login prompt:
        </p>
        <ThreeDotMenu
          rowIndex={0}
          expandedRow={expandedRow}
          setExpandedRow={setExpandedRow}
          onShowMore={() => {}}
          onSaveSearch={() => Promise.resolve()}
          onDownload={() => {}}
        />
      </div>
    );
  },
};

// Save Search Flow (Logged In)
export const SaveSearchLoggedIn = {
  render: () => {
    const [expandedRow, setExpandedRow] = useState(null);
    const [saveCount, setSaveCount] = useState(0);

    // Set session storage to simulate logged in
    sessionStorage.setItem('idToken', 'mock-token-for-storybook');

    const handleSave = () => {
      setSaveCount(saveCount + 1);
      return Promise.resolve();
    };

    return (
      <div>
        <div
          style={{
            padding: '1rem',
            background: '#d4edda',
            border: '1px solid #28a745',
            borderRadius: '8px',
            marginBottom: '1rem',
          }}
        >
          <p style={{ margin: 0, fontWeight: '600', color: '#155724' }}>
            ✓ Simulating logged in (idToken present)
          </p>
        </div>
        <p style={{ marginBottom: '1rem' }}>
          Saves completed: <strong>{saveCount}</strong>
        </p>
        <p style={{ marginBottom: '1rem' }}>
          Click the menu and select "Save Search Query" to see the success modal:
        </p>
        <ThreeDotMenu
          rowIndex={0}
          expandedRow={expandedRow}
          setExpandedRow={setExpandedRow}
          onShowMore={() => {}}
          onSaveSearch={handleSave}
          onDownload={() => {}}
        />
      </div>
    );
  },
};

// All Menu Actions
export const AllMenuActions = {
  render: () => {
    const [expandedRow, setExpandedRow] = useState(null);
    const [actions, setActions] = useState([]);

    const logAction = (action) => {
      setActions([...actions, `${new Date().toLocaleTimeString()}: ${action}`]);
    };

    return (
      <div>
        <p style={{ marginBottom: '1rem', fontWeight: '600' }}>
          Try all menu options:
        </p>
        <ThreeDotMenu
          rowIndex={0}
          expandedRow={expandedRow}
          setExpandedRow={(index) => {
            setExpandedRow(index);
            logAction(
              index === 0 ? 'Expanded row' : 'Collapsed row'
            );
          }}
          onShowMore={() => logAction('Show more clicked')}
          onSaveSearch={() => {
            logAction('Save search clicked');
            return Promise.resolve();
          }}
          onDownload={() => logAction('Download clicked')}
        />

        {actions.length > 0 && (
          <div
            style={{
              marginTop: '2rem',
              padding: '1rem',
              background: '#f5f5f5',
              borderRadius: '8px',
            }}
          >
            <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>
              Action Log:
            </p>
            <ul style={{ margin: 0, paddingLeft: '1.5rem', fontSize: '0.875rem' }}>
              {actions.map((action, index) => (
                <li key={index}>{action}</li>
              ))}
            </ul>
          </div>
        )}
      </div>
    );
  },
};
