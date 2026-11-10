import styles from "./RowContainer.module.css";

//  Renders an expandable table row with a provided info component.
//    {boolean} isExpanded - Whether this row should be visible
//    {JSX.Element} expandedContent - The component to display inside the expanded area
//    {number} colSpan - Number of columns to span; this depends on how many td in the main header.

const ExpandedRowContainer = ({ isExpanded, expandedContent, colSpan }) => {
  if (!isExpanded) return null;

  return (
    <tr className={styles.expandedRow}>
      <td colSpan={colSpan}>
        <div className={styles.expandedContent}>
          {expandedContent}
        </div>
      </td>
    </tr>
  );
};

export default ExpandedRowContainer;