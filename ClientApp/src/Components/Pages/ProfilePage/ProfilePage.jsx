import styles from './ProfilePage.module.css';
import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import SavedSearches from '../../SavedSearches/SavedSearches';
import AccountSettings from '../../AccountSettings/AccountSettings';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faBookmark, faPerson } from '@fortawesome/free-solid-svg-icons';
import AdminDashboard from '../../AdminDashboard/AdminDashboard';
import { API_BASES, apiUrl } from '../../../api/config';

const ProfilePage = () => {

  const navigate = useNavigate();



  async function checkAdmin() {
            try {
                const idToken = sessionStorage.getItem("idToken");

                if (!idToken) {
                    console.log("No idToken found");
                    setIsAdmin(false);
                    setLoadingAdmin(false);
                    return;
                }

                const res = await fetch(apiUrl(API_BASES.login, "/api/auth/is-admin"), {
                    headers: {
                        "Authorization": `Bearer ${idToken}`
                    }
                });

                if (!res.ok) {
                    console.error("Admin check failed with status:", res.status);
                    setIsAdmin(false);
                    setLoadingAdmin(false);
                    return;
                }

                const data = await res.json();
                console.log("Admin check response:", data);
                setIsAdmin(data.isAdmin === true);
            } catch (err) {
                console.error("Admin check failed:", err);
                setIsAdmin(false);
            } finally {
                setLoadingAdmin(false);
            }
        }

  
  useEffect(() => {
    const idToken = sessionStorage.getItem('idToken');
    if (!idToken) {
      navigate('/Login');
    }
    
    checkAdmin();
  }, [navigate]);

  const [activeTab, setActiveTab] = useState('account');
  const [isAdmin, setIsAdmin] = useState(false);
  const [loadingAdmin, setLoadingAdmin] = useState(true);


  return (
    <div className={styles.page}>
      <div className={styles.container}>
        <div className={styles.pageHeader}>
          <h1 className={styles.pageTitle}>Profile Settings</h1>
          <p className={styles.pageSubtitle}>Manage your account settings and preferences</p>
        </div>

        {/* Tab Navigation */}
        <div className={styles.tabContainer}>
          <nav className={styles.tabNav}>
            <button
              onClick={() => setActiveTab('account')}
              className={`${styles.tab} ${activeTab === 'account' ? styles.tabActive : ''}`}
            >
              <FontAwesomeIcon icon={faPerson}/>
              <span>Account</span>
            </button>
            <button
              onClick={() => setActiveTab('searches')}
              className={`${styles.tab} ${activeTab === 'searches' ? styles.tabActive : ''}`}
            >
              <FontAwesomeIcon icon={faBookmark}/>
              <span>Saved Searches</span>
            </button>
            {isAdmin && (
                          <button
                              onClick={() => setActiveTab('admin')}
                              className={`${styles.tab} ${activeTab === 'admin' ? styles.tabActive : ''}`}
                          >
                              <FontAwesomeIcon icon={faBookmark} />
                              <span>Admin Dashboard</span>
                          </button>
                      )}
          </nav>
        </div>

        {/* Main Content Area */}
        <div className={styles.content}>
          {activeTab === 'account' && <AccountSettings />}
          {activeTab === 'searches' && <SavedSearches />}
          {activeTab === 'admin' && isAdmin && <AdminDashboard />}
        </div>
      </div>
    </div>
  );
};

export default ProfilePage;
