import Card from "../Card";
import styles from './CardRow.module.css';

// takes an array of cards and creates a row-based layout
// example of how to use:
    /* <CardRow
        cards={[
            {
                title: "Genomes",
                body: <p>View and analyze genomic data.</p>,
                footer: <button>Explore</button>
            },
            {
                title: "Results",
                body: <p>Check analysis results and statistics.</p>,
                footer: <button>View</button>
            },
            {
                title: "Reports",
                body: <p>Generate detailed summaries.</p>,
                footer: <button>Download</button>
            }
        ]}
    /> */


const CardRow = ({cards}) => {
    return(
        <div className={styles.cardRow}>
            {cards && cards.map((card, index) => (
                <Card
                key={index}
                title={card.title}
                body={card.body}
                footer={card.footer}
                icon={card.icon}
                />
            ))}
        </div>
    );
}

export default CardRow;
