import React from 'react';
import styles from '../../Pages/DemoResultsPage/DemoResultsPage.module.css';

// One responsive results surface for saved demonstrations and connected histories.
export default function ResultsWorkspace({children}) {
    return <div className={styles.workspace}>{children}</div>;
}
