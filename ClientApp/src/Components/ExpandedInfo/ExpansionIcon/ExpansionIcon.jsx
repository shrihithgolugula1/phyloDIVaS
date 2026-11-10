import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faPlus, faMinus } from "@fortawesome/free-solid-svg-icons";
import styles from "./ExpansionIcon.module.css";

//  A reusable expansion toggle icon for table rows.
//    @param {function} setExpandedRow - State setter function to control expanded row.
//    @param {number|null} expandedRow - Current expanded row index.
//    @param {number} rowIndex - The index for this row.

const ExpansionIcon = ({ setExpandedRow, expandedRow, rowIndex }) => {
  return (
    <div
      className={styles.showMore}
      onClick={() =>
        setExpandedRow(expandedRow === rowIndex ? null : rowIndex)
      }
    >
      <FontAwesomeIcon icon={expandedRow === rowIndex ? faMinus : faPlus} />
    </div>
  );
};

export default ExpansionIcon;
