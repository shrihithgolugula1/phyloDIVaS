import React, { useState, useEffect } from 'react';
import styles from './SavedSearches.module.css';
import Button from '../Button/Button';
import {getSearchHistory, deleteSearch} from "../../api/searchHistoryApiCalls.js";
import { useNavigate } from 'react-router-dom';

const SavedSearches = () => {
  // Mock data 
  // const searches = ([
  //   {
  //     id: 1,
  //     query: 'The query',
  //     createdDate: '11/1/25',
  //     resultCount: 247
  //   },
  //   {
  //     id: 2,
  //     query: 'The query',
  //     createdDate: '11/1/25',
  //     resultCount: 247
  //   },
  //   {
  //     id: 3,
  //     query: 'The query',
  //     createdDate: '11/1/25',
  //     resultCount: 247
  //   },
  //   {
  //     id: 4,
  //     query: 'The query',
  //     createdDate: '11/1/25',
  //     resultCount: 247
  //   }
  // ]);
  const userId = sessionStorage.getItem('idToken');

  
  const [searches, setSearches] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);


  async function loadSearches() {
      try {
        const data = await getSearchHistory(userId);
        setSearches(data);
      } catch (err) {
        console.error("Error fetching searches:", err);
        setError("Failed to load searches");
      } finally {
        setLoading(false);
      }
    }

    useEffect(() => {
    
    loadSearches();
  }, [userId]);


  const handleDelete = async (id) => {
    try {
      await deleteSearch(userId, id);
      //refresh the table
      loadSearches();
    } catch (err) {
      console.error("Error deleting search:", err);
    }
  };

  const getLastSearch = async (Search) =>{
    
    sessionStorage.setItem('lastSearch', Search.query_text);
    console.log("check session: " + sessionStorage.getItem('lastSearch'));
    //navigator.navigate('/Search');

    if(Search.search_type === "genome"){
      window.location.href = '/Search'
    }else{
      console.log(Search.resolved_tax_id)
      const getTaxon = await fetch(`https://api.ncbi.nlm.nih.gov/datasets/v2/taxonomy/taxon/${Search.resolved_tax_id}/dataset_report`,{
          method: "GET",
          headers: { "Content-Type": "application/json" },
        })
      const data = await getTaxon.json()
      console.log(data.reports[0].taxonomy.current_scientific_name.name)
      window.location.href = '/GeneSearch?taxon='+encodeURIComponent(data.reports[0].taxonomy.current_scientific_name.name)
    }
  }



  return (
    <div className={styles.body}>
      <div className={styles.savedSearchSection}>
        <div className={styles.savedSearchContainer}>
          <div className={styles.sectionHeader}>
            <h2 className={styles.sectionTitle}>Saved Searches</h2>
            <p>Manage your saved search queries</p>
          </div>

          {searches.length === 0 ? (
            <div className={styles.emptyState}>
              {/* search icon */}
              <p>No saved searches yet</p>
            </div>
          ) : (
            <table className={styles.savedSearchesTable}>
              <thead>
                <tr>
                  <th className={styles.headerCell}>Query</th>
                  <th className={styles.headerCell}>Resolved ID</th>
                  <th className={styles.headerCell}>Created</th>
                  <th className={`${styles.headerCell} ${styles.actionsHeader}`}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {searches.map((search) => (
                  <tr key={search.search_id}>
                    <td className={`${styles.tableCell} ${styles.queryCell}`}>
                      {search.query_text}
                    </td>
                    <td className={styles.tableCell}>
                      <span >{search.resolved_assembly_id}</span>
                    </td>
                    <td className={`${styles.tableCell} ${styles.dateCell}`}>
                      {new Date(search.searched_at).toLocaleDateString('en-US')}
                    </td>
                    <td className={`${styles.tableCell} ${styles.actionsCell}`}>
                      <Button onClick={() => getLastSearch(search)} label={"Load"} color={"blue"}/>
                      <Button onClick={() => handleDelete(search.search_id)} label={"Delete"} color={"pink"}/>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
};

export default SavedSearches;
