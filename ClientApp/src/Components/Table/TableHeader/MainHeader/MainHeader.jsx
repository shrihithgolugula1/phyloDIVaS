import styles from "./MainHeader.module.css";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faGear } from "@fortawesome/free-solid-svg-icons";
//  Renders the top-level headers
//    Accepts header data as JSON

//  Expects id, label, colspan (which is the # of subheaders to that parent header)
//    Example JSON:
//    [
//      { id: 1, label: "Organism", colspan: 2},
//      { id: 2, label: "Assembly", colspan: 2 },
//      { id: 3, label: "Other Info", colspan: 1 }

const MainHeader = ({ headers }) => {
    return (
        <tr>
            {headers.map((header) => (
                <th className={styles.mainHeader} key={header.id} colSpan={header.colspan || 1}>
                    {header.label}
                </th>
            ))}
            <th className={styles.mainHeader}>
                Actions
            </th>
        </tr>
    );
};

export default MainHeader;
