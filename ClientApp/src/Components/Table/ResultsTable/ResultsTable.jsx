import React, { useState } from "react";
import MainHeader from "../TableHeader/MainHeader/MainHeader";
import SubHeader from "../TableHeader/SubHeader/SubHeader";
import TableRow from "../TableRow/TableRow";
import styles from "./ResultsTable.module.css";

const ResultsTable = ({ rows, headers, subHeaders, includeSubHeader, expandedContent, columns }) => {
    const [expandedRow, setExpandedRow] = useState(null);

    if (!rows || rows.length === 0) {
        return null; // Don't show anything if no results (handled by parent)
    }

    return (
        <div className={styles.tableRegion} role="region" aria-label="Search results" tabIndex="0">
        <table className={styles.resultsTable}>
            <thead>
                <MainHeader headers={headers} />
                {includeSubHeader && <SubHeader subHeaders={subHeaders} />}
            </thead>

            <tbody>
                {rows.map((rowData, index) => (
                    <TableRow
                        key={index}
                        rowData={rowData}
                        expandedRow={expandedRow}
                        setExpandedRow={setExpandedRow}
                        rowIndex={index}
                        expandedContent={expandedContent}
                        columns={columns}
                    />
                ))}
            </tbody>
        </table>
        </div>
    );
};

export default ResultsTable;
