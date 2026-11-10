import LinkTo from "../../Link/Link";
import styles from "./AboutPage.module.css";

const AboutPage = () => {
    return (
        <div className={styles.aboutWrapper}>
            <section className={styles.hero}>
                <div className={styles.heroContent}>
                    <p className={styles.eyebrow}>About phyloDIVaS</p>
                    <h1 className={styles.title}>Advancing comparative genomics for everyone</h1>
                    <p className={styles.subtitle}>
                        phyloDIVaS is a student-led research platform from Temple University,
                        built to make genomic search, analysis, and collaboration intuitive for
                        scientists and learners alike.
                    </p>
                </div>
            </section>

            <section className={styles.grid}>
                <div className={styles.card}>
                    <h2>What we do</h2>
                    <p>
                        We bring together sequence search, phylogenetic analysis, and clear visual
                        summaries so you can move from curiosity to insight without switching tools.
                    </p>
                    <ul className={styles.list}>
                        <li>Genome and gene search with curated public data sources</li>
                        <li>Side-by-side comparisons across species and assemblies</li>
                        <li>Downloadable results for figures, papers, and presentations</li>
                    </ul>
                </div>

                <div className={styles.card}>
                    <h2>Our mission</h2>
                    <p>
                        Our mission is to lower the barrier to phylogenomic exploration by pairing reliable data with
                        approachable workflows. We focus on clarity, reproducibility, and teaching
                        support so new users and experts can collaborate.
                    </p>
                    <div className={styles.tagList}>
                        <span className={styles.tag}>Open data</span>
                        <span className={styles.tag}>Education first</span>
                        <span className={styles.tag}>Reproducible</span>
                    </div>
                </div>

                <div className={styles.card}>
                    <h2>Who we are</h2>
                    <p>
                        A cross-disciplinary team of students, faculty, and mentors at Temple
                        University. We build features based on feedback from researchers,
                        classrooms, and community partners.
                    </p>
                    <p className={styles.note}>
                        Have an idea or want to collaborate? Contact us through the feedback form.
                    </p>
                </div>
            </section>

            <section className={styles.infoStrip}>
                <div className={styles.infoBlock}>
                    <p className={styles.metric}>Built for</p>
                    <p className={styles.metricValue}>Search · Analysis · Teaching</p>
                </div>
                <div className={styles.infoBlock}>
                    <p className={styles.metric}>Supported by</p>
                    <p className={styles.metricValue}>Temple University</p>
                </div>
                <div className={styles.infoBlock}>
                    <p className={styles.metric}>Get involved</p>
                    <p className={styles.metricValue}>Feedback and feature requests are welcome</p>
                </div>
            </section>

            <section className={styles.ctaSection}>
                <div className={styles.ctaCard}>
                    <div>
                        <h3>Ready to explore?</h3>
                        <p>
                            Jump into a genome search or start a new analysis. You can always return
                            here to learn more about the project as it grows.
                        </p>
                    </div>
                    <div className={styles.ctaActions}>
                        <LinkTo label="Go to Search" linkTo="/Search" color="grey" />
                        <LinkTo label="Run an Analysis" linkTo="/Analysis" color="grey" />
                    </div>
                </div>
            </section>
        </div>
    );
};

export default AboutPage;
