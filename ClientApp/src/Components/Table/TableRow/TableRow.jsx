import ExpansionIcon from "../../ExpandedInfo/ExpansionIcon/ExpansionIcon";
import ExpandedRowContainer from "../../ExpandedInfo/ExpandedContainer/RowContainer/RowContainer";
import ThreeDotMenu from "../../ThreeDots/ThreeDotMenu";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faChevronDown, faChevronUp, faDownload, faDna } from "@fortawesome/free-solid-svg-icons";
import styles from "./TableRow.module.css";

//  Displays a single row of data
//  Expanded components are called here

const TableRow = ({ rowData, expandedRow, setExpandedRow, rowIndex, expandedContent, columns }) => {
    const isExpanded = expandedRow === rowIndex;
    const visibleColumns = columns || Object.keys(rowData)
        .filter(key => !key.startsWith('_'))
        .map(key => ({ key, render: (row) => row[key] }));


    return (
        <>
            <tr className={styles.dataRow}>
                {visibleColumns.map((column) => {
                        const value = column.render ? column.render(rowData) : rowData[column.key];
                        if (columns) {
                            return <td key={column.key}>{value}</td>;
                        }
                        if (typeof value === "object" && value !== null) {
                            return Object.values(value).map((v, i) => (
                                <td key={`${column.key}-${i}`}>{v}</td>
                            ));
                        }
                        return <td key={column.key}>{value}</td>;
                    })}
                <td className={styles.actionsCell}>
                    <button
                        className={styles.primaryAction}
                        onClick={() => setExpandedRow(isExpanded ? null : rowIndex)}
                        aria-expanded={isExpanded}
                    >
                        <FontAwesomeIcon icon={isExpanded ? faChevronUp : faChevronDown} />
                        {isExpanded ? "Less" : "Details"}
                    </button>
                    {rowData._geneSearchAction && (
                        <button className={styles.iconAction} onClick={() => rowData._geneSearchAction(rowData)} title="Search genes" aria-label="Search genes">
                            <FontAwesomeIcon icon={faDna} />
                        </button>
                    )}
                    {rowData._downloadAction && (
                        <button className={styles.iconAction} onClick={() => rowData._downloadAction(rowData)} title="Download data" aria-label="Download data">
                            <FontAwesomeIcon icon={faDownload} />
                        </button>
                    )}
                    <ThreeDotMenu
                        onSaveSearch={() => rowData._saveAction(rowData._fullData)}
                    />
                </td>
            </tr>

            <ExpandedRowContainer
                isExpanded={isExpanded}
                expandedContent={typeof expandedContent === 'function' ? expandedContent(rowData) : expandedContent}
                colSpan={visibleColumns.length + 1}
            />
        </>
    );
};

export default TableRow;
