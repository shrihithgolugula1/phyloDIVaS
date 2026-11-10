import styles from "./SubHeader.module.css";
//  Optional secondary header row
//    Should only render if includeSubHeader = true in ResultsTable

//  Expects parent and the labels that go under it
//  Example JSON:
//  [
//      { parent: "Organism", labels: ["Name", "Common Name"] },
//      { parent: "Assembly", labels: ["Name", "Level"] }
//  ]

const SubHeader = ({ subHeaders }) => {
    return (
        <tr>
            {subHeaders.map((section, index) =>
                section.labels.map((label, i) => (
                    <th className={styles.subHeader} key={`${index}-${i}`}>{label}</th>
                ))
            )}
        </tr>
    );
};

export default SubHeader;